﻿//
// Copyright (c) 2007 Ole André Vadla Ravnås <oleavr@gmail.com>
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.ComponentModel;
using System.Threading;
using System.Collections.Generic;
using System.Xml;
using System.IO;

namespace oSpy.SharpDumpLib
{
    public delegate void AnalyzeProgressChangedEventHandler(object sender, AnalyzeProgressChangedEventArgs e);
    public delegate void AnalyzeCompletedEventHandler(object sender, AnalyzeCompletedEventArgs e);

    public class DumpAnalyzer : AsyncWorker
    {
        #region Events

        public event AnalyzeProgressChangedEventHandler AnalyzeProgressChanged;
        public event AnalyzeCompletedEventHandler AnalyzeCompleted;

        #endregion // Events

        #region Internal members

        private delegate void WorkerEventHandler(Dump dump, AsyncOperation asyncOp, SendOrPostCallback completionMethodDelegate);
        private WorkerEventHandler workerDelegate;

        #endregion // Internal members

        #region Construction and destruction

        public DumpAnalyzer()
            : base()
        {
        }

        public DumpAnalyzer(IContainer container)
            : base(container)
        {
        }

        #endregion // Construction and destruction

        #region Public interface

        public virtual List<Resource> Analyze(Dump dump)
        {
            return DoAnalysis(dump, null);
        }

        public virtual void AnalyzeAsync(Dump dump, object taskId)
        {
            AsyncOperation asyncOp = CreateOperation(taskId);

            workerDelegate = new WorkerEventHandler(AnalyzeWorker);
            workerDelegate.BeginInvoke(dump, asyncOp, completionMethodDelegate, null, null);
        }

        public virtual void AnalyzeAsyncCancel(object taskId)
        {
            CancelOperation(taskId);
        }

        #endregion // Public interface

        #region Async glue

        private void AnalyzeWorker(Dump dump, AsyncOperation asyncOp, SendOrPostCallback completionMethodDelegate)
        {
            List<Resource> resources = null;
            Exception e = null;

            try
            {
                resources = DoAnalysis(dump, asyncOp);
            }
            catch (Exception ex)
            {
                e = ex;
            }

            AnalyzeDumpState analyzeState = new AnalyzeDumpState(dump, resources, e, asyncOp);

            try { completionMethodDelegate(analyzeState); }
            catch (InvalidOperationException) { }
        }

        protected override object CreateCancelEventArgs(object userSuppliedState)
        {
            return new AnalyzeCompletedEventArgs(null, null, null, true, userSuppliedState);
        }

        protected override void ReportProgress(object e)
        {
            OnAnalyzeProgressChanged(e as AnalyzeProgressChangedEventArgs);
        }

        protected virtual void OnAnalyzeProgressChanged(AnalyzeProgressChangedEventArgs e)
        {
            if (AnalyzeProgressChanged != null)
                AnalyzeProgressChanged(this, e);
        }

        protected override void ReportCompletion(object e)
        {
            OnAnalyzeCompleted(e as AnalyzeCompletedEventArgs);
        }

        protected virtual void OnAnalyzeCompleted(AnalyzeCompletedEventArgs e)
        {
            if (AnalyzeCompleted != null)
                AnalyzeCompleted(this, e);
        }

        protected override void CompletionMethod(object state)
        {
            AnalyzeDumpState analyzeState = state as AnalyzeDumpState;

            AsyncOperation asyncOp = analyzeState.asyncOp;
            AnalyzeCompletedEventArgs e = new AnalyzeCompletedEventArgs(analyzeState.dump, analyzeState.resources, analyzeState.ex, false, asyncOp.UserSuppliedState);
            FinalizeOperation(asyncOp, e);
        }

        #endregion // Async glue

        #region Core implementation

        private List<Resource> DoAnalysis(Dump dump, AsyncOperation asyncOp)
        {
            List<Resource> resources = new List<Resource>();

            SortedDictionary<uint, Event> pendingEvents = new SortedDictionary<uint, Event>(dump.Events);
            List<Event> processedEvents = new List<Event>();
            int n = 0;
            int numEvents = dump.Events.Count;

            while (pendingEvents.Count > 0)
            {
                Resource curRes = null;

                foreach (Event ev in pendingEvents.Values)
                {
                    bool processed = false;

                    if (ev.Type == DumpEventType.FunctionCall)
                    {
                        XmlElement data = ev.Data;

                        ResourceType resType;
                        bool endOfLifetime;
                        UInt32 handle;
                        if (GetResourceTypeAndHandle(data, out resType, out endOfLifetime, out handle))
                        {
                        }
                    }
                    else
                    {
                        processed = true;
                    }

                    if (processed)
                    {
                        processedEvents.Add(ev);
                        n++;
                    }
                }

                //if (processedEvents.Count == 0)
                //    throw new InvalidDataException(String.Format("{0} pending events could not be processed", pendingEvents.Count));

                foreach (Event ev in processedEvents)
                {
                    pendingEvents.Remove(ev.Id);
                }
                processedEvents.Clear();

                break;
            }

            return resources;
        }

        private bool GetResourceTypeAndHandle(XmlElement data, out ResourceType type, out bool endOfLifetime, out UInt32 handle)
        {
            endOfLifetime = false;

            XmlNode node = data.SelectSingleNode("/event/name");
            if (node == null)
                throw new InvalidDataException("name element not found on FunctionCall event");
            string functionName = node.InnerText.ToLower();

            if (functionName == "ws2_32.dll::socket")
            {
                type = ResourceType.Socket;

                node = data.SelectSingleNode("/event/returnValue/value/@value");
                if (node == null)
                    throw new InvalidDataException("returnValue element not found on FunctionCall event");
                handle = 0; // UInt32.Parse(node.Value);
            }
            else if (functionName == "ws2_32.dll::close")
            {
                type = ResourceType.Socket;
                endOfLifetime = true;
                /*
                node = data.SelectSingleNode("/event/arguments[@direction=in]/value/@value");
                if (node == null)
                    throw new InvalidDataException("returnValue element not found on FunctionCall event");*/
                handle = Convert.ToUInt32(node.Value);
            }
            else
            {
                type = ResourceType.Unknown;
                endOfLifetime = false;
                handle = 0;
                return false;
            }

            return true;
        }

        #endregion // Core implementation
    }

    #region Helper classes

    internal class AnalyzeDumpState
    {
        public Dump dump = null;
        public List<Resource> resources = null;
        public Exception ex = null;
        public AsyncOperation asyncOp = null;

        public AnalyzeDumpState(Dump dump, List<Resource> resources, Exception ex, AsyncOperation asyncOp)
        {
            this.dump = dump;
            this.resources = resources;
            this.ex = ex;
            this.asyncOp = asyncOp;
        }
    }

    public class AnalyzeProgressChangedEventArgs : ProgressChangedEventArgs
    {
        private Resource latestResource = null;
        public Resource LatestResource
        {
            get { return latestResource; }
        }

        public AnalyzeProgressChangedEventArgs(Resource latestResource, int progressPercentage, object userToken)
            : base(progressPercentage, userToken)
        {
            this.latestResource = latestResource;
        }
    }

    public class AnalyzeCompletedEventArgs : AsyncCompletedEventArgs
    {
        private Dump dump = null;
        public Dump Dump
        {
            get
            {
                RaiseExceptionIfNecessary();
                return dump;
            }
        }

        private List<Resource> resources = null;
        public List<Resource> Resources
        {
            get
            {
                RaiseExceptionIfNecessary();
                return resources;
            }
        }

        public AnalyzeCompletedEventArgs(Dump dump, List<Resource> resources, Exception e, bool cancelled, object state)
            : base(e, cancelled, state)
        {
            this.dump = dump;
            this.resources = resources;
        }
    }

    #endregion // Helper classes
}
