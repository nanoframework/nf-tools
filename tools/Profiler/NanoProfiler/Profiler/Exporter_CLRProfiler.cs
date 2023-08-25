using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using _PRF = nanoFramework.Tools.NanoProfiler;

namespace nanoFramework.Tools.NanoProfiler
{
    public class Exporter_CLRProfiler : Exporter
    {
        private bool _processedFirstEvent;

        private Dictionary<uint, uint>          _methodIdLookup;
        private Dictionary<ObjectType, ulong>   _typeIdLookup;

        private uint _nextMethodId;
        private ulong _nextTypeId;
        private ulong _nextCallStackId;

        private ulong _startTime;
        private ulong _lastWrittenTime;

        public Exporter_CLRProfiler(
            ProfilerSession ps,
            string file) : base(ps, file)
        {
            _methodIdLookup = new Dictionary<uint, uint>();
            _typeIdLookup = new Dictionary<ObjectType, ulong>();

            _nextMethodId = 1;
            _nextTypeId = 1;
            _nextCallStackId = 1;

            ps.OnEventAdd += new ProfilerSession.OnProfilerEventAddHandler(ProcessEvent);
        }

        internal void ProcessEvent(_PRF.ProfilerSession ps, _PRF.ProfilerEvent pe)
        {
            ulong callstack;

            if (_processedFirstEvent == false)
            {
                _processedFirstEvent = true;
                _startTime = pe.Time;
                _lastWrittenTime = pe.Time;
            }
            else
            {
                if (pe.Time >= _lastWrittenTime + 1)
                {   
                    // Mimic the 5-ms granularity of the CLRProfiler.
                    _sw.WriteLine("i {0}", pe.Time - _startTime);
                    _lastWrittenTime = pe.Time;
                }
            }

            switch (pe.Type)
            {
                case _PRF.ProfilerEvent.EventType.HeapDump:
                    {
                        _PRF.HeapDump hd = (_PRF.HeapDump)pe;
                        foreach (_PRF.HeapDumpRoot hdr in hd.RootTable)
                        {
                            uint md = 0;

                            if (hdr._type == HeapDumpRoot.RootType.Stack)
                            {
                                /* This needs to come first because it has the side effect of writing a
                                 * method definition out if one doesn't exist for it yet.
                                 */
                                md = FindMethod(ps, hdr._method);
                            }

                            _sw.Write($"e 0x{hdr._address:x8} ");

                            switch (hdr._type)
                            {
                                case HeapDumpRoot.RootType.Finalizer:
                                    _sw.Write("2 ");
                                    break;

                                case HeapDumpRoot.RootType.Stack:
                                    _sw.Write("1 ");
                                    break;

                                default:
                                    _sw.Write("0 ");
                                    break;
                            }

                            switch (hdr._flags)
                            {
                                case HeapDumpRoot.RootFlags.Pinned:
                                    _sw.Write("1 ");
                                    break;

                                default:
                                    _sw.Write("0 ");
                                    break;
                            }

                            switch (hdr._type)
                            {
                                case HeapDumpRoot.RootType.Stack:
                                    _sw.WriteLine("{0}", md);
                                    break;

                                default:
                                    _sw.WriteLine("0");
                                    break;
                            }
                        }

                        foreach (_PRF.HeapDumpObject hdo in hd.ObjectTable)
                        {
                            ulong typeid = FindType(ps, hdo._type);

                            _sw.Write($"o 0x{hdo._address:x8} {typeid} {hdo._size}");

                            if (hdo._references != null)
                            {
                                foreach (uint ptr in hdo._references)
                                {
                                    _sw.Write($" 0x{ptr:x8}");
                                }
                            }

                            _sw.WriteLine();
                        }
                        break;
                    }

                case _PRF.ProfilerEvent.EventType.Call:
                    {
                        _PRF.FunctionCall f = (_PRF.FunctionCall)pe;
                        callstack = MakeCallStack(ps, f.m_callStack);

                        _sw.WriteLine($"c {f._thread} {callstack}");
                        break;
                    }

                case _PRF.ProfilerEvent.EventType.Return:
                case _PRF.ProfilerEvent.EventType.ContextSwitch:
                    //The CLRProfiler does not care about function timing; it's primary goal is to display memory usage over time, and barely considers function calls
                    break;

                case _PRF.ProfilerEvent.EventType.Allocation:
                    {
                        _PRF.ObjectAllocation a = (_PRF.ObjectAllocation)pe;

                        callstack = MakeCallStack(ps, a._callStack, a._objectType, a._size);
                        _sw.WriteLine($"! {a._thread} 0x{a._address:x} {callstack}");

                        break;
                    }

                case _PRF.ProfilerEvent.EventType.Relocation:
                    {
                        _PRF.ObjectRelocation r = (_PRF.ObjectRelocation)pe;
                        for (uint i = 0; i < r._relocationRegions.Length; i++)
                        {
                            _sw.WriteLine($"u 0x{r._relocationRegions[i]._start:x} 0x{r._relocationRegions[i]._start + r._relocationRegions[i]._offset:x} {r._relocationRegions[i]._end - r._relocationRegions[i]._start}");
                        }
                        break;
                    }

                case _PRF.ProfilerEvent.EventType.Deallocation:
                    {
                        _PRF.ObjectDeletion d = (_PRF.ObjectDeletion)pe;
                        break;
                    }

                case _PRF.ProfilerEvent.EventType.GarbageCollectionBegin:
                    {
                        _PRF.GarbageCollectionBegin gc = (_PRF.GarbageCollectionBegin)pe;

                        uint lastObjAddress = ps._liveObjectTable[ps._liveObjectTable.Count - 1] + 1;
                        _sw.WriteLine($"b 1 0 0 0x{ps.HeapStart:x} {lastObjAddress} {ps.HeapBytesReserved} 0");
                        
                        break;
                    }

                case _PRF.ProfilerEvent.EventType.GarbageCollectionEnd:
                    {
                        _PRF.GarbageCollectionEnd gc = (_PRF.GarbageCollectionEnd)pe;

                        for (int i = 0; i < gc.liveObjects.Count; i++)
                        {
                            //Send length of 1 for single object, regardless of true object length.
                            _sw.WriteLine($"v 0x{gc.liveObjects[i]:x} 1");
                        }

                        uint lastObjAddress = ps._liveObjectTable[ps._liveObjectTable.Count - 1] + 1;
                        _sw.WriteLine($"b 0 0 0 0x{ps.HeapStart:x} {lastObjAddress} {ps.HeapBytesReserved} 0");

                        break;
                    }
                case _PRF.ProfilerEvent.EventType.HeapCompactionBegin:
                    {
                        _PRF.HeapCompactionBegin gc = (_PRF.HeapCompactionBegin)pe;

                        uint lastObjAddress = ps._liveObjectTable[ps._liveObjectTable.Count - 1] + 1;
                        _sw.WriteLine($"b 1 0 0 0x{ps.HeapStart:x} {lastObjAddress} {ps.HeapBytesReserved} 0");

                        break;
                    }

                case _PRF.ProfilerEvent.EventType.HeapCompactionEnd:
                    {
                        _PRF.HeapCompactionEnd gc = (_PRF.HeapCompactionEnd)pe;

                        uint lastObjAddress = ps._liveObjectTable[ps._liveObjectTable.Count - 1] + 1;
                        _sw.WriteLine($"b 0 0 0 0x{ps.HeapStart:x} {lastObjAddress} {ps.HeapBytesReserved} 0");

                        break;
                    }
            }

            _sw.Flush();
        }

        private ulong FindType(_PRF.ProfilerSession ps, ObjectType type)
        {
            if (_typeIdLookup.ContainsKey(type))
            {
                return _typeIdLookup[type];
            }
            else
            {
                //FIXME: Need to know which types are finalizable
                StringBuilder typeName = new StringBuilder(ps.ResolveTypeName(type.Type));

                for (int i = 0; i < type.Rank; i++)
                {
                    typeName.Append("[]");
                }

                ulong typeid = _nextTypeId++;
                _typeIdLookup.Add(type, typeid);
                _sw.WriteLine($"t {typeid} 0 {typeName.ToString()}");

                return typeid;
            }
        }

        private uint FindMethod(_PRF.ProfilerSession ps, uint md)
        {
            if (_methodIdLookup.ContainsKey(md))
            {
                return _methodIdLookup[md];
            }
            else
            {
                uint methodid = _nextMethodId++;

                _methodIdLookup.Add(md, methodid);

                string methodName;

                try
                {
                    methodName = ps._engine.GetMethodName(md, true);
                }
                catch
                {
                    methodName = "UNKNOWN METHOD";
                }

                _sw.WriteLine($"f {methodid} {methodName} (UNKNOWN_ARGUMENTS) 0 0");
                
                return methodid;
            }
        }

        private ulong MakeCallStack(ProfilerSession ps, uint[] callStack)
        {
            return MakeCallStackInternal(ps, callStack, "0");   //"0" means no referenced call stack or type or size.
        }

        private ulong MakeCallStack(ProfilerSession ps, uint[] callStack, ObjectType type, uint size)
        {
            ulong typeid = FindType(ps, type);

            //"1" means a type id and size come before the call stack.
            string flags = $"1 {typeid} {size}";  
            
            return MakeCallStackInternal(ps, callStack, flags);
        }

        private ulong MakeCallStackInternal(ProfilerSession ps, uint[] callStack, string flags)
        {
            uint[] transStack = new uint[callStack.Length];
            ulong id = _nextCallStackId;
            _nextCallStackId++;

            for (uint i = 0; i < callStack.Length; i++)
            {
                transStack[i] = FindMethod(ps, callStack[i]);
            }

            _sw.Write($"n {id} {flags}");

            for (uint i = 0; i < transStack.Length; i++)
            {
                _sw.Write($" {transStack[i]}");
            }

            _sw.WriteLine();

            return id;
        }
    }
}
