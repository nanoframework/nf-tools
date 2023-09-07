using nanoFramework.Tools.Debugger.WireProtocol;
using Polly;
using Polly.Contrib.WaitAndRetry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using _DBG = nanoFramework.Tools.Debugger;
using _WP = nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.NanoProfiler
{
    // Look into ways of compacting data...

    public class ProfilerSession
    {
        internal const uint HeapBlockSize = 12;

        private bool _connected;
        internal HeapDump _currentHeapDump;

        internal bool _firstPacket;
        internal ushort _lastSeenStreamPacketID;

        internal Dictionary<uint, Stack<uint>> _threadCallStacks;
        internal uint _currentThreadPID;
        internal uint _currentAssembly;

        internal SortedDictionary<uint, string> _liveObjectTable;

        private Thread _receiverThread;
        private _DBG.BitStream _incomingStream;

        internal _DBG.Engine _engine;
        private bool _heapAddressIsAbsolute;
        private uint _heapStart;

        public delegate void OnProfilerEventAddHandler(ProfilerSession ps, ProfilerEvent pe);
        public event OnProfilerEventAddHandler OnEventAdd;
        public event EventHandler OnDisconnect;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="engine">Debugger engine for <see cref="ProfilerSession"/>.</param>
        /// <param name="heapAbsoluteAddress">Use absolute memory address for Heap.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public ProfilerSession(_DBG.Engine engine, bool heapAbsoluteAddress = false)
        {
            _connected = true;
            _engine = engine ?? throw new ArgumentNullException();
            _heapAddressIsAbsolute = heapAbsoluteAddress;
            _engine.OnCommand += new _DBG.CommandEventHandler(OnDeviceCommand);
            _incomingStream = new _DBG.BitStream(true);

            StartTime = 0;
            LastKnownTime = 0;
            _currentHeapDump = null;
            _threadCallStacks = new Dictionary<uint, Stack<uint>>();

            _liveObjectTable = new SortedDictionary<uint, string>();

            _firstPacket = true;

            _receiverThread = new Thread(WorkerThread);
            _receiverThread.Start();
        }

        public void EnableProfiling()
        {
            _engine.SetProfilingMode(_WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Enabled, 0);

            //Move IsDeviceInInitializeState(), IsDeviceInExitedState(), GetDeviceState(),EnsureProcessIsInInitializedState() to Debugger.dll?
           
            if(!_engine.SetExecutionMode(0, 0))
            {
                // failed to stop execution
                throw new Exception("Failed to stop execution");
            }

            //Device should be stopped when we try to build the stack traces.

            uint[] threads = _engine.GetThreadList();

            if (threads != null)
            {
                for (int i = 0; i < threads.Length; i++)
                {
                    _WP.Commands.Debugging_Thread_Stack.Reply reply = _engine.GetThreadStack(threads[i]);

                    if (reply != null)
                    {
                        Stack<uint> stack = new Stack<uint>();

                        for (int j = 0; j < reply.m_data.Length; j++)
                        {
                            stack.Push(reply.m_data[j].m_md);
                        }

                        _threadCallStacks.Add(threads[i], stack);
                    }
                }
            }
        }

        public void SetProfilingOptions(
            bool calls,
            bool allocations)
        {
            uint set = 0;
            uint reset = 0;

            if (_engine.Capabilities.ProfilingCalls)
            {
                if (calls)
                {
                    set |= _WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Calls;
                }
                else
                {
                    reset |= _WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Calls;
                }
            }

            if (_engine.Capabilities.ProfilingAllocations)
            {
                if (allocations)
                {
                    set |= _WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Allocations;
                }
                else
                {
                    reset |= _WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Allocations;
                }
            }

            _engine.SetProfilingMode(set, reset);
        }

        public void DisableProfiling()
        {
            _engine.SetProfilingMode(0,
                _WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Enabled |
                _WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Calls |
                _WP.Commands.Profiling_Command.ChangeConditionsFlags.c_Allocations
                );
        }

        public void Disconnect()
        {
            if (_connected)
            {
                _connected = false;
                try
                {
                    DisableProfiling();
                    /* When this 'flush' returns, all data in the stream should have been sent out.
                     * so setting the end of stream marker will not cause race conditions with the WP packet receiving thread.
                     */
                    _engine.FlushProfilingStream();
                }
                catch { /* Ignore errors if we are already disconnected from the device. */}

                _incomingStream.MarkStreamEnd();
            }
        }

        /* Make sure that Disconnect gets called, otherwise EndOfStreamException will never get thrown and it will block forever. */
        private void WorkerThread()
        {
            while (true)
            {
                try
                {
                    Packets.ProfilerPacket pp = ProfilerPacketFactory.Decode(_incomingStream);
                    pp.Process(this);

                    // Don't write out incomplete lines if we lose the device during processing? Insert 'safe' values instead?
                }
                catch (System.IO.IOException)
                {
                    /* The CLR is allowed to resume/quit now; we have all data we need. */
                    try
                    {
                        _engine.ResumeExecution();
                    }
                    catch { }

                    /*We've been disconnected -- shutdown thread. */
                    if (OnDisconnect != null) { OnDisconnect(this, EventArgs.Empty); }
                    return;
                }
            }
        }

#if DEBUG
        public ulong MaxProfilePayloadLength { get; private set; } = 0;
#endif

        public ulong BitsReceived { get; private set; }

        public ulong StartTime { get; internal set; }

        public ulong LastKnownTime { get; internal set; }

        public uint HeapStart
        {
            get => _heapStart;

            internal set
            {
                _heapStart = HeapAddressIsAbsolute ? value : 0;
            }
        }

        public uint HeapBytesUsed { get; internal set; }

        public uint HeapBytesReserved { get; internal set; }

        public uint HeapBytesFree
        {
            get { return HeapBytesReserved - HeapBytesUsed; }

            internal set
            {
                if (HeapBytesReserved > value)
                {
                    HeapBytesUsed = HeapBytesReserved - value;
                }
            }
        }

        public bool HeapAddressIsAbsolute => _heapAddressIsAbsolute;

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void OnDeviceCommand(
            _WP.IncomingMessage msg,
            bool fReply)
        {
            if (!fReply)
            {
                switch (msg.Header.Cmd)
                {
                    case _WP.Commands.c_Profiling_Stream:
                        _WP.Commands.Profiling_Stream pay = (_WP.Commands.Profiling_Stream)msg.Payload;
                        //Some sort of packet ordering with 'pay.seqId' to gurantee packets are need to arrived in the correct order and aren't lost.
                        if (_firstPacket)
                        {
                            _firstPacket = false;
                            _lastSeenStreamPacketID = pay.seqId;
                        }
                        else
                        {
                            if (pay.seqId == 0)
                            {
                                //Sometimes the device manages to send out a packet before the device restarts and manages to throw off sequence id checking.
                                _lastSeenStreamPacketID = 0;
                            }
                            else
                            {
                                System.Diagnostics.Debug.Assert(pay.seqId == _lastSeenStreamPacketID + 1);
                                _lastSeenStreamPacketID = pay.seqId;
                            }
                        }

#if DEBUG
                        // update stats on the stream
                        // usefull for debugging purposes only
                        MaxProfilePayloadLength = Math.Max(MaxProfilePayloadLength, pay.bitLen);
#endif

                        BitsReceived += pay.bitLen;
                        _incomingStream.AppendChunk(pay.payload, 0, pay.bitLen);
                        break;
                }
            }
        }

        internal void AddEvent(ProfilerEvent pe)
        {
            pe.Time = LastKnownTime;

            //Call event to allow for output streaming.
            try
            {
                OnEventAdd(this, pe);
            }
            catch { }
        }

        internal string ResolveTypeName(uint type)
        {
            if ((type & 0xffff0000) == 0)
            {
                //We have a DataType, not a TypeDef index.
                return ((_DBG.nanoClrDataType)type).ToString();
            }
            else
            {
                // setup an exponential backoff retry policy to handle the case where the debugger may be overwhelmed with requests
                var backoffDelay = Backoff.ExponentialBackoff(TimeSpan.FromMilliseconds(500), retryCount: 20);

                var retryPolicy = Policy
                    .HandleResult<Commands.Debugging_Resolve_Type.Result>(r => string.IsNullOrEmpty(r?.m_name))
                    .WaitAndRetry(backoffDelay);

                var resolvedType = retryPolicy.Execute(() =>
                {
                    return _engine.ResolveType(type);
                });

                System.Diagnostics.Debug.Assert(resolvedType != null);

                return resolvedType.m_name;
            }
        }
    }

    public class ProfilerEvent
    {
        private ulong _time;

        public enum EventType
        {
            HeapDump = 0x00,
            Call = 0x01,
            Return = 0x02,
            ContextSwitch = 0x03,
            Allocation = 0x04,
            Relocation = 0x05,
            Deallocation = 0x06,
            GarbageCollectionBegin = 0x07,
            GarbageCollectionEnd = 0x08,
            HeapCompactionBegin = 0x09,
            HeapCompactionEnd = 0x0a,
        }

        public ulong Time
        {
            get { return _time; }
            internal set { _time = value; }
        }
        private EventType m_type;

        public EventType Type
        {
            get { return m_type; }
            set { m_type = value; }
        }
    }

    /*
     * There's got to be a better way to separate all these commands from the wire-protocol's deserialization
     * and whichever way we want to export the data.
     * It seems a combination of Model-View-Controller and Command patterns is appropriate, but I don't have time to refactor.
     */

    public class HeapDump : ProfilerEvent
    {
        internal List<HeapDumpObject> _objectTable;
        internal List<HeapDumpRoot> _rootTable;

        public HeapDump()
        {
            base.Type = EventType.HeapDump;
            _objectTable = new List<HeapDumpObject>();
            _rootTable = new List<HeapDumpRoot>();
        }

        public IList<HeapDumpObject> ObjectTable
        {
            get { return _objectTable.AsReadOnly(); }
        }

        public IList<HeapDumpRoot> RootTable
        {
            get { return _rootTable.AsReadOnly(); }
        }
    }

    public class HeapDumpRoot
    {
        public enum RootType
        {
            Finalizer,
            AppDomain,
            Assembly,
            Thread,
            Stack,
        }

        [Flags]
        public enum RootFlags
        {
            None = 0,
            Pinned = 1,
        }

        public uint _address;
        public RootType _type;
        public RootFlags _flags;
        public uint _method;
    }

    public class HeapDumpObject
    {
        public uint _address;
        public ObjectType _type;
        public uint _size;
        public List<uint> _references;
    }

    public class ObjectType
    {
        private uint _type;
        private ushort _arrayRank;

        public ObjectType(uint type)
        {
            _type = type;
            _arrayRank = 0;
        }

        public ObjectType(uint type, ushort arrayRank)
        {
            _type = type;
            _arrayRank = arrayRank;
        }

        public uint Type
        {
            get { return _type; }
        }

        public ushort Rank
        {
            get { return _arrayRank; }
        }

        public override bool Equals(object obj)
        {
            //Check for null and compare run-time types.
            if (obj == null || GetType() != obj.GetType()) return false;
            ObjectType hdot = (ObjectType)obj;
            return _type == hdot._type && _arrayRank == hdot._arrayRank;
        }

        public override int GetHashCode()
        {
            /* From MSDN Object.GetHashCode():
             * For derived classes of Object, GetHashCode can delegate to the Object.GetHashCode implementation, if and only if
             * that derived class defines value equality to be reference equality and the type is not a value type.
             */
            return _arrayRank.GetHashCode() ^ _type.GetHashCode();
        }
    }

    public class FunctionCall : ProfilerEvent
    {
        public uint _thread;
        public uint[] m_callStack;

        public FunctionCall()
        {
            base.Type = EventType.Call;
        }
    }

    public class FunctionReturn : ProfilerEvent
    {
        public uint _thread;
        public ulong duration;

        public FunctionReturn()
        {
            base.Type = EventType.Return;
        }
    }

    public class ContextSwitch : ProfilerEvent
    {
        public uint _thread;

        public ContextSwitch()
        {
            base.Type = EventType.ContextSwitch;
        }
    }

    public class ObjectAllocation : ProfilerEvent
    {
        public uint _thread;
        public uint _address;
        public uint[] _callStack;
        public uint _size;
        public ObjectType _objectType;

        public ObjectAllocation()
        {
            base.Type = EventType.Allocation;
        }
    }

    public class ObjectRelocation : ProfilerEvent
    {
        public class RelocationRegion
        {
            public uint _start;
            public uint _end;
            public uint _offset;
        }

        public RelocationRegion[] _relocationRegions;

        public ObjectRelocation()
        {
            base.Type = EventType.Relocation;
        }
    }

    public class ObjectDeletion : ProfilerEvent
    {
        public uint address;

        public ObjectDeletion()
        {
            base.Type = EventType.Deallocation;
        }
    }

    public class GarbageCollectionBegin : ProfilerEvent
    {
        public GarbageCollectionBegin()
        {
            base.Type = EventType.GarbageCollectionBegin;
        }
    }

    public class GarbageCollectionEnd : ProfilerEvent
    {
        public SortedDictionary<uint, string> liveObjects;

        public GarbageCollectionEnd()
        {
            base.Type = EventType.GarbageCollectionEnd;
        }
    }

    public class HeapCompactionBegin : ProfilerEvent
    {
        public HeapCompactionBegin()
        {
            base.Type = EventType.HeapCompactionBegin;
        }
    }

    public class HeapCompactionEnd : ProfilerEvent
    {
        public HeapCompactionEnd()
        {
            base.Type = EventType.HeapCompactionEnd;
        }
    }

    public abstract class Exporter
    {
        protected FileStream _fs;
        protected StreamWriter _sw;
        protected string _fileName;

        public Exporter(ProfilerSession ps, string file)
        {
            if (!string.IsNullOrEmpty(file))
            {
                _fileName = file;
            }
            else
            {
                _fileName = Path.GetTempFileName();
            }

            _fs = new FileStream(_fileName, FileMode.Create, FileAccess.Write, FileShare.Read);
            _sw = new StreamWriter(_fs);
        }

        public virtual void Close()
        {
            //Need a way to explicitly close the file; otherwise file doesn't get closed until finalizer runs and then user can't overwrite to the same file.
            _sw.Close();
        }

        public string FileName => _fileName;
    }
}
