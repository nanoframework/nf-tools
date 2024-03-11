using CLRProfiler;
using Microsoft.VisualBasic.Logging;
using nanoFramework.Tools.NanoProfiler.CLRProfiler;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.Services;

public class ReadLogResultService
{
    internal Font font;
    private long logFileStartOffset;
    private long logFileEndOffset;
    internal ReadNewLog log;
    internal ReadLogResult lastLogResult;
    internal static MainForm instance;
    internal string prevlogFileName;
    internal string currlogFileName;
    internal Graph.GraphType graphtype = Graph.GraphType.Invalid;
    internal bool runaswindow = false;

    private ReadLogResult GetLogResult()
    {
        ReadLogResult readLogResult = lastLogResult;
        if (readLogResult == null)
        {
            readLogResult = new ReadLogResult();
        }
        readLogResult.liveObjectTable = new LiveObjectTable(log);
        readLogResult.sampleObjectTable = new SampleObjectTable(log);
        readLogResult.allocatedHistogram = new Histogram(log);
        readLogResult.callstackHistogram = new Histogram(log);
        readLogResult.relocatedHistogram = new Histogram(log);
        readLogResult.finalizerHistogram = new Histogram(log);
        readLogResult.criticalFinalizerHistogram = new Histogram(log);
        readLogResult.createdHandlesHistogram = new Histogram(log);
        readLogResult.destroyedHandlesHistogram = new Histogram(log);

        if (readLogResult.objectGraph != null)
        {
            readLogResult.objectGraph.Neuter();
        }

        readLogResult.objectGraph = new ObjectGraph(log, 0);
        readLogResult.functionList = new FunctionList(log);
        readLogResult.hadCallInfo = readLogResult.hadAllocInfo = false;
        readLogResult.handleHash = new Dictionary<ulong, HandleInfo>();

        // We may just have turned a lot of data into garbage - let's try to reclaim the memory
        GC.Collect();

        return readLogResult;
    }

    public ReadLogResult LoadLogFile(string logFileName)
    {
        if (!string.IsNullOrWhiteSpace(currlogFileName))
        {
            (prevlogFileName, currlogFileName) = (currlogFileName, logFileName);
        }
        else
        {
            currlogFileName = logFileName;
        }

        logFileStartOffset = 0;
        logFileEndOffset = long.MaxValue;
        log = new ReadNewLog(logFileName);
        ObjectGraph.cachedGraph = null;
        ReadLogResult readLogResult = GetLogResult();
        log.ReadFile(logFileStartOffset, logFileEndOffset, readLogResult);
        lastLogResult = readLogResult;


        return lastLogResult;
    }

    public ReadNewLog GetReadNewLog() => log;

    public string GetPreviousLogFileName() => prevlogFileName;

}
