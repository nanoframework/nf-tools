////
// Copyright (c) .NET Foundation and Contributors.
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
////

using CLRProfiler;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    [Obsolete("Has been replaced by SummaryView")]
    public class MainForm
    {
        internal Font font;
        private string logFileName;
        private long logFileStartOffset;
        private long logFileEndOffset;
        internal ReadNewLog log;
        internal ReadLogResult lastLogResult;
        internal static MainForm instance;
        internal string prevlogFileName;
        internal string currlogFileName;
        internal Graph.GraphType graphtype = Graph.GraphType.Invalid;
        internal bool runaswindow = false;


        public MainForm()
        {
            instance = this;
            font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((System.Byte)(204))); ;
        }

        internal void LoadLogFile(string logFileName)
        {
            this.logFileName = logFileName;
            logFileStartOffset = 0;
            logFileEndOffset = long.MaxValue;

            log = new ReadNewLog(logFileName);
            lastLogResult = null;
            ObjectGraph.cachedGraph = null;
            ReadLogResult readLogResult = GetLogResult();
            log.ReadFile(logFileStartOffset, logFileEndOffset, readLogResult);
            lastLogResult = readLogResult;

            viewSummaryMenuItem_Click(null, null);
        }

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
                readLogResult.objectGraph.Neuter();

            readLogResult.objectGraph = new ObjectGraph(log, 0);
            readLogResult.functionList = new FunctionList(log);
            readLogResult.hadCallInfo = readLogResult.hadAllocInfo = false;
            readLogResult.handleHash = new Dictionary<ulong, HandleInfo>();

            // We may just have turned a lot of data into garbage - let's try to reclaim the memory
            GC.Collect();

            return readLogResult;
        }

        private void viewSummaryMenuItem_Click(object sender, System.EventArgs e)
        {
            if (lastLogResult != null)
            {
                string scenario = log.fileName;

                SummaryForm summaryForm = new SummaryForm(log, lastLogResult, scenario);
                summaryForm.Show();
            }
        }
    }
}
