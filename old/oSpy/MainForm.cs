//
// Copyright (c) 2006-2007 Ole Andr� Vadla Ravn�s <oleavr@gmail.com>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System.Windows.Forms;
using System.Data;
using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text;
using System.IO;
using ICSharpCode.SharpZipLib.BZip2;
using System.Text.RegularExpressions;
using oSpy.Capture;
using oSpy.Configuration;
using oSpy.Event;
using oSpy.Parser;
using oSpy.Util;
using oSpy.Net;
using System.Threading;
using System.Xml;
using System.Diagnostics;

namespace oSpy
{
    public partial class MainForm : Form
    {
        private ConfigContext config;
        private Capture.Manager captureMgr;

        //private DataTable tblMessages;
        private PacketParser packetParser;
        private List<TCPEvent> tmpEventList;
        private List<IPPacket> tmpPacketList;

        //private Dictionary<int, IPPacket> curLinesToPackets;
        private List<IPPacket> curPacketList;
        private TransactionNodeList curNodeList;
        private MemoryStream curSelBytes;

        public enum DisplayMode
        {
            ASCII,
            HEX,
        };

        private DisplayMode dumpDisplayMode;
        public DisplayMode DumpDisplayMode
        {
            get
            {
                return dumpDisplayMode;
            }

            set
            {
                dumpDisplayMode = value;
                UpdateDumpView();
            }
        }
        
        private DebugForm debugForm;
        private SoftwallForm swForm;

        private bool updatingSelections = false;
        private PacketSlice[] prevSelectedSlices;

        private ColorPool colorPool;
#if false
        private string deviceLabel;
        private string statusLabel;
        private string subStatusLabel;
        private string wizStatusLabel;
#endif

        public MainForm()
        {
            InitializeComponent();

            config = ConfigManager.GetContext("MainForm");
            captureMgr = new Capture.Manager();

            colorPool = new ColorPool();

            //tblMessages = dataSet.Tables["messages"];

            debugForm = new DebugForm();
            swForm = new SoftwallForm();

            packetParser = new PacketParser(debugForm);
            packetParser.PacketDescriptionReceived += new PacketParser.PacketDescriptionReceivedHandler(packetParser_PacketDescriptionReceived);

            dumpDisplayMode = DisplayMode.HEX;

            findTypeComboBox.SelectedIndex = 0;

            ClearState();
            LoadSettings();
            ApplyFilters();
        }

        protected void ClearState()
        {
#if false
            deviceLabel = "";
            statusLabel = "";
            subStatusLabel = "";
            wizStatusLabel = "";
#endif

            dataSet.Clear();
            packetParser.Reset();
            prevSelectedSlices = null;
            curNodeList = null;
            curPacketList = null;
            curSelBytes = new MemoryStream();
            tmpEventList = new List<TCPEvent>(8);
            tmpPacketList = new List<IPPacket>(32);
            richTextBox.Clear();
            propertyGrid.SelectedObject = null;
        }

        protected void LoadSettings()
        {
            if (config.HasSetting("Location"))
                Location = (Point) config["Location"];
            if (config.HasSetting("Size"))
                Size = (Size) config["Size"];

            if (config.HasSetting("MainSplitPos"))
                mainSplitContainer.SplitterDistance = (int)config["MainSplitPos"];
            if (config.HasSetting("LowerSplitPos"))
                lowerSplitContainer.SplitterDistance = (int)config["LowerSplitPos"];

            if (config.HasSetting("ViewInternalDebugChecked"))
                viewInternalDebugToolStripMenuItem.Checked = (bool)config["ViewInternalDebugChecked"];
            if (config.HasSetting("ViewWinCryptChecked"))
                viewWinCryptToolStripMenuItem.Checked = (bool)config["ViewWinCryptChecked"];

            foreach (DataGridViewColumn col in dataGridView.Columns)
            {
                if (col.Visible)
                {
                    string name = col.Name + "Width";

                    if (config.HasSetting(name))
                        col.Width = (int)config[name];
                }
            }

            if (config.HasSetting("FilterText"))
                filterComboBox.Text = (string)config["FilterText"];

            if (config.HasSetting("FindTypeIndex"))
                findTypeComboBox.SelectedIndex = (int)config["FindTypeIndex"];
            if (config.HasSetting("FindText"))
                findComboBox.Text = (string)config["FindText"];
        }

        protected void SaveSettings()
        {
            config["Location"] = Location;
            config["Size"] = Size;

            config["MainSplitPos"] = mainSplitContainer.SplitterDistance;
            config["LowerSplitPos"] = lowerSplitContainer.SplitterDistance;

            config["ViewInternalDebugChecked"] = viewInternalDebugToolStripMenuItem.Checked;
            config["ViewWinCryptChecked"] = viewWinCryptToolStripMenuItem.Checked;

            foreach (DataGridViewColumn col in dataGridView.Columns)
            {
                if (col.Visible)
                {
                    config[col.Name + "Width"] = col.Width;
                }
            }

            config["FilterText"] = filterComboBox.Text;

            config["FindTypeIndex"] = findTypeComboBox.SelectedIndex;
            config["FindText"] = findComboBox.Text;
        }

        private void packetParser_PacketDescriptionReceived(IPPacket[] packets, string description)
        {
            for (int i = 0; i < packets.Length; i++)
            {
                DataRow row = dataSet.Tables[0].Select(String.Format("Index = {0}", packets[i].Index))[0];

                if (packets.Length == 1)
                    row["Comment"] = description;
                else if (i == 0)
                    row["Comment"] = String.Format("<{0}", description);
                else if (i == packets.Length - 1)
                    row["Comment"] = String.Format("{0}>", description);
                else
                    row["Comment"] = String.Format("...{0}...", description);
            }
        }

#if false
        private void listener_ElementsReceived(AgentListener.MessageQueueElement[] elements)
        {
            if (InvokeRequired)
            {
                Invoke(receivedHandler, new object[] { elements });
                return;
            }

            object source = dataGridView.DataSource;
            dataGridView.DataSource = null;
            dataSet.Tables[0].BeginLoadData();

            foreach (AgentListener.MessageQueueElement msg in elements)
            {
                DataTable tbl = dataSet.Tables["messages"];

                DataRow row = tbl.NewRow();
                row.BeginEdit();

                /* Common stuff */
                row["Timestamp"] = new DateTime(msg.time.wYear, msg.time.wMonth,
                                                msg.time.wDay, msg.time.wHour,
                                                msg.time.wMinute, msg.time.wSecond,
                                                msg.time.wMilliseconds,
                                                DateTimeKind.Local);

                row["ProcessName"] = msg.process_name;
                row["ProcessId"] = msg.process_id;
                row["ThreadId"] = msg.thread_id;

                row["FunctionName"] = msg.function_name;
                row["Backtrace"] = msg.backtrace;

                UInt32 returnAddress = 0;
                string callerModName = "";

                if (msg.backtrace.Length > 0)
                {
                    string[] tokens = msg.backtrace.Split(new char[] { '\n' }, 2);
                    if (tokens.Length >= 1)
                    {
                        string line = tokens[0];
                        string[] lineTokens = line.Split(new string[] { "::" }, 2, StringSplitOptions.None);

                        if (lineTokens.Length == 2)
                        {
                            returnAddress = Convert.ToUInt32(lineTokens[1].Substring(2), 16);
                            callerModName = lineTokens[0];
                        }
                    }
                }

                row["ReturnAddress"] = returnAddress;
                row["CallerModuleName"] = callerModName;

                row["ResourceId"] = msg.resource_id;

                row["MsgType"] = msg.msg_type;

                row["MsgContext"] = msg.context;
                row["Domain"] = msg.domain;
                row["Severity"] = msg.severity;
                row["Message"] = msg.message;

                if (msg.context == MessageContext.MESSAGE_CTX_ACTIVESYNC_DEVICE)
                    deviceLabel = msg.message;
                else if (msg.context == MessageContext.MESSAGE_CTX_ACTIVESYNC_STATUS)
                    statusLabel = msg.message;
                else if (msg.context == MessageContext.MESSAGE_CTX_ACTIVESYNC_SUBSTATUS)
                    subStatusLabel = msg.message;
                else if (msg.context == MessageContext.MESSAGE_CTX_ACTIVESYNC_WZ_STATUS)
                    wizStatusLabel = msg.message;

                row["Direction"] = msg.direction;
                row["LocalAddress"] = msg.local_address;
                row["LocalPort"] = msg.local_port;
                row["PeerAddress"] = msg.peer_address;
                row["PeerPort"] = msg.peer_port;

                byte[] data = new byte[msg.len];
                Array.Copy(msg.buf, data, data.Length);
                row["Data"] = data;

                row["AS_Device"] = deviceLabel;
                row["AS_Status"] = statusLabel;
                row["AS_SubStatus"] = subStatusLabel;
                row["AS_WizStatus"] = wizStatusLabel;

                row.EndEdit();

                tbl.Rows.Add(row);
            }

            dataSet.Tables[0].EndLoadData();
            dataGridView.DataSource = source;
        }

        void listener_Stopped()
        {
            if (InvokeRequired)
            {
                Invoke(stoppedHandler);
                return;
            }

            newCaptureToolStripMenuItem.Enabled = true;
        }
#endif

        private void exitMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void openMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                ProgressForm progFrm = new ProgressForm("Opening");

                ClearState();

                dataGridView.DataSource = null;
                dataSet.Tables[0].BeginLoadData();

                clearMenuItem.PerformClick();

                Thread th = new Thread(new ParameterizedThreadStart(OpenFile));
                th.Start(progFrm);

                progFrm.ShowDialog(this);
            }
        }

        private void OpenFile(object param)
        {
            IProgressFeedback progress = param as IProgressFeedback;

            Capture.DumpFile df = new Capture.DumpFile();
            df.Load(openFileDialog.FileName, progress);

            foreach (DumpEvent ev in df.Events.Values)
            {
                DataRow row = dataSet.Tables[0].NewRow();
                row[eventCol] = ev;
                row.AcceptChanges();
                dataSet.Tables[0].Rows.Add(row);
            }

            dataSet.Tables[0].EndLoadData();

            progress.ProgressUpdate("Opened", 100);

            progress.ProgressUpdate("Finishing", 100);
            Invoke(new ThreadStart(RestoreDataSource));

            progress.OperationComplete();


#if false
            IProgressFeedback progress = param as IProgressFeedback;

            FileStream fs = new FileStream(openFileDialog.FileName, FileMode.Open);
            BZip2InputStream stream = new BZip2InputStream(fs);

            dataSet.ReadXml(stream);

            stream.Close();
            fs.Close();

            dataSet.Tables[0].EndLoadData();

            progress.ProgressUpdate("Opened", 100);

            AnalyzePackets(progress);

            progress.ProgressUpdate("Finishing", 100);
            Invoke(new ThreadStart(RestoreDataSource));

            progress.OperationComplete();
#endif
        }

        private void RestoreDataSource()
        {
            dataGridView.DataSource = bindingSource;
        }

        private void AnalyzePackets(object param)
        {
            IProgressFeedback progress = param as IProgressFeedback;

            //
            // Find all the sessions and create a PacketStream for each
            //
            Dictionary<UInt32, IPSession> sessions = new Dictionary<UInt32, IPSession>();
            List<UInt32> sessionList = new List<UInt32>();
            IPSession session;

            progress.ProgressUpdate("Identifying sessions", 0);

            int i = 0;
            foreach (IPPacket p in tmpPacketList)
            {
                UInt32 id = (UInt32) (p.ResourceId + p.RemoteEndpoint.Port);

                if (sessions.ContainsKey(id))
                    session = sessions[id];
                else
                {
                    session = new IPSession();
                    sessions[id] = session;
                    sessionList.Add(id);
                }

                session.AddPacket(p);
                i++;

                progress.ProgressUpdate("Identifying sessions",
                    (int) ((i / (float) tmpPacketList.Count) * 100.0f));
            }

            //
            // Add the events to the appropriate sessions
            //

            // First off, sort them by timestamp
            tmpEventList.Sort();

            // Then match them with the sessions
            foreach (TCPEvent ev in tmpEventList)
            {
                if (sessions.ContainsKey(ev.ResourceId))
                    sessions[ev.ResourceId].AddEvent(ev);
            }

            // Don't need these any more now
            tmpEventList.Clear();
            tmpPacketList.Clear();

            //
            // Iterate over them, in sequence, and hand each over to the parser.
            //
            i = 0;
            foreach (UInt32 id in sessionList)
            {
                session = sessions[id];
                packetParser.AddSession(session);

                i++;

                progress.ProgressUpdate("Parsing sessions",
                    (int)((i / (float)sessionList.Count) * 100.0f));
            }
        }

        private void saveMenuItem_Click(object sender, EventArgs e)
        {
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(saveFileDialog.FileName, FileMode.Create);
                BZip2OutputStream stream = new BZip2OutputStream(fs);
                dataSet.WriteXml(stream);
                stream.Close();
                fs.Close();
            }
        }

        private void newCaptureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Capture.ChooseForm frm = new Capture.ChooseForm();

            Process[] processes = frm.GetSelectedProcesses();
            if (processes.Length == 0)
                return;

            ProgressForm progFrm = new ProgressForm("Starting capture");

            captureMgr.StartCapture(processes, progFrm);

            if (progFrm.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show(String.Format("Failed to start capture: {0}", progFrm.GetOperationErrorMessage()),
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Capture.ProgressForm capProgFrm = new Capture.ProgressForm(captureMgr);
            capProgFrm.ShowDialog();

            progFrm = new ProgressForm("Stopping capture");

            captureMgr.StopCapture(progFrm);

            if (progFrm.ShowDialog() != DialogResult.OK)
            {
                MessageBox.Show(String.Format("Failed to stop capture: {0}", progFrm.GetOperationErrorMessage()),
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                captureMgr.SaveCapture(saveFileDialog.FileName);
            }
            else
            {
                captureMgr.DiscardCapture();
            }

#if false
            tmpEventList.Clear();
            tmpPacketList.Clear();

            captureStartMenuItem.Enabled = false;

            object source = dataGridView.DataSource;
            dataGridView.DataSource = null;

            CaptureForm frm = new CaptureForm(listener, swForm.GetRules());
            frm.ShowDialog(this);

            dataGridView.DataSource = null;

            ProgressForm progFrm = new ProgressForm("Analyzing data");

            Thread th = new Thread(new ParameterizedThreadStart(DoPostAnalysis));
            th.Start(progFrm);

            progFrm.ShowDialog(this);
#endif
        }

        private void DoPostAnalysis(object param)
        {
            IProgressFeedback progress = param as IProgressFeedback;

            AnalyzePackets(progress);

            progress.ProgressUpdate("Finishing", 100);
            Invoke(new ThreadStart(RestoreDataSource));

            progress.OperationComplete();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            SaveSettings();
            ConfigManager.Save();
        }

        private void dataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            DataGridViewRow row = dataGridView.Rows[e.RowIndex];
            DumpEvent ev = row.Cells[eventTextCol.Index].Value as DumpEvent;

            switch (e.ColumnIndex)
            {
                case 1:
                    e.Value = ev.Id;
                    break;
                case 2:
                    e.Value = ev.Timestamp;
                    break;
                case 3:
                    e.Value = ev.Type.ToString();
                    break;
            }
        }

#if false
        private void dataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            DataGridViewRow row = dataGridView.Rows[e.RowIndex];
            MessageType msg_type = (MessageType)((UInt32)row.Cells[msgTypeDataGridViewTextBoxColumn.Index].Value);
            bool highlight = false;

            if (msg_type == MessageType.MESSAGE_TYPE_MESSAGE &&
                e.ColumnIndex >= 1 && e.ColumnIndex <= 2)
            {
                MessageContext context = (MessageContext)((UInt32) row.Cells[msgContextDataGridViewTextBoxColumn.Index].Value);

                if (context == MessageContext.MESSAGE_CTX_ACTIVESYNC_DEVICE ||
                    context == MessageContext.MESSAGE_CTX_ACTIVESYNC_STATUS ||
                    context == MessageContext.MESSAGE_CTX_ACTIVESYNC_SUBSTATUS ||
                    context == MessageContext.MESSAGE_CTX_ACTIVESYNC_WZ_STATUS)
                {
                    highlight = true;
                    e.CellStyle.ForeColor = Color.White;
                }
            }

            if (highlight || e.ColumnIndex == 1)
            {
                try
                {
                    byte[] c = (byte[])row.Cells[bgColorDataGridViewTextBoxColumn.Index].Value;
                    e.CellStyle.BackColor = Color.FromArgb(c[0], c[1], c[2]);
                }
                catch (InvalidCastException)
                {
                }
            }

            switch (e.ColumnIndex)
            {
                case 1:
                    if (msg_type == MessageType.MESSAGE_TYPE_MESSAGE)
                    {
                        MessageContext context = (MessageContext)((UInt32) row.Cells[msgContextDataGridViewTextBoxColumn.Index].Value);

                        switch (context)
                        {
                            case MessageContext.MESSAGE_CTX_INFO:
                                e.Value = Properties.Resources.InfoImg;
                                break;
                            case MessageContext.MESSAGE_CTX_WARNING:
                                e.Value = Properties.Resources.WarningImg;
                                break;
                            case MessageContext.MESSAGE_CTX_ERROR:
                                e.Value = Properties.Resources.ErrorImg;
                                break;
                            case MessageContext.MESSAGE_CTX_SOCKET_LISTENING:
                                e.Value = Properties.Resources.ListeningImg;
                                break;
                            case MessageContext.MESSAGE_CTX_SOCKET_CONNECTING:
                                e.Value = Properties.Resources.ConnectingImg;
                                break;
                            case MessageContext.MESSAGE_CTX_SOCKET_CONNECTED:
                                e.Value = Properties.Resources.ConnectedImg;
                                break;
                            case MessageContext.MESSAGE_CTX_SOCKET_DISCONNECTED:
                            case MessageContext.MESSAGE_CTX_SOCKET_RESET:
                                e.Value = Properties.Resources.DisconnectedImg;
                                break;
                            case MessageContext.MESSAGE_CTX_ACTIVESYNC_DEVICE:
                            case MessageContext.MESSAGE_CTX_ACTIVESYNC_STATUS:
                            case MessageContext.MESSAGE_CTX_ACTIVESYNC_SUBSTATUS:
                            case MessageContext.MESSAGE_CTX_ACTIVESYNC_WZ_STATUS:
                                e.Value = Properties.Resources.ActiveSyncImg;
                                break;
                        }
                    }
                    else
                    {
                        PacketDirection direction = (PacketDirection)((UInt32) row.Cells[directionDataGridViewTextBoxColumn.Index].Value);

                        if (direction == PacketDirection.PACKET_DIRECTION_INCOMING)
                        {
                            e.Value = Properties.Resources.IncomingImg;
                        }
                        else if (direction == PacketDirection.PACKET_DIRECTION_OUTGOING)
                        {
                            e.Value = Properties.Resources.OutgoingImg;
                        }
                        else
                        {
                            e.Value = Properties.Resources.InfoImg;
                        }
                    }
                    break;
                case 8:
                    UInt32 retAddr = (UInt32)
                        row.Cells[returnAddressDataGridViewTextBoxColumn.Index].Value;

                    if (retAddr == 0)
                    {
                        e.Value = "N/A";
                    }
                    else
                    {
                        string procName = (string)
                            row.Cells[processNameDataGridViewTextBoxColumn.Index].Value;
                        string callerModName = (string)
                            row.Cells[callerModuleNameDataGridViewTextBoxColumn.Index].Value;
                        string backTrace = (string)
                            row.Cells[backtraceDataGridViewTextBoxColumn.Index].Value;

                        string retAddrStr = String.Format("0x{0:x8}", retAddr);

                        string firstBtLine = backTrace.Split(new char[] { '\n' }, 2)[0];
                        string[] tokens = firstBtLine.Split(new char[] { ' ' }, 2);
                        if (tokens.Length > 1)
                            retAddrStr = tokens[1].Substring(1, tokens[1].Length - 2);

                        if (procName == callerModName)
                        {
                            e.Value = retAddrStr;
                        }
                        else
                        {
                            e.Value = String.Format("{0} [{1}]", retAddrStr, callerModName);
                        }
                    }

                    break;
            }
        }
#endif

        private void clearMenuItem_Click(object sender, EventArgs e)
        {
            ClearState();
        }

        private void dataGridView_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {

        }

        private void messageTbl_RowChanged(object sender, DataRowChangeEventArgs e)
        {
            if (e.Action == DataRowAction.Add)
            {
                e.Row.BeginEdit();

                e.Row["Sender"] = String.Format("{0} [pid={1}, tid={2}]",
                                                e.Row["ProcessName"],
                                                e.Row["ProcessId"],
                                                e.Row["ThreadId"]);

                int index = (int) e.Row["Index"];
                DateTime timestamp = (DateTime) e.Row["Timestamp"];

                UInt32 resourceId = (UInt32) e.Row["ResourceId"];
                PacketDirection direction = (PacketDirection)((UInt32)e.Row["Direction"]);

                byte[] data = (byte[]) e.Row["Data"];
                string suffix = (data.Length > 1) ? "s" : "";

                IPEndpoint localEndpoint = new IPEndpoint((string) e.Row["LocalAddress"],
                                                          (UInt16) e.Row["LocalPort"]);
                IPEndpoint remoteEndpoint = new IPEndpoint((string)e.Row["PeerAddress"],
                                                           (UInt16)e.Row["PeerPort"]);

                if ((MessageType) ((UInt32) e.Row["MsgType"]) == MessageType.MESSAGE_TYPE_MESSAGE)
                {
                    MessageContext context = (MessageContext) ((UInt32) e.Row["MsgContext"]);
                    string s = "";

                    switch (context)
                    {
                        case MessageContext.MESSAGE_CTX_ACTIVESYNC_DEVICE:
                            s = "DeviceLabel = ";
                            break;
                        case MessageContext.MESSAGE_CTX_ACTIVESYNC_STATUS:
                            s = "StatusLabel = ";
                            break;
                        case MessageContext.MESSAGE_CTX_ACTIVESYNC_SUBSTATUS:
                            s = "SubStatusLabel = ";
                            break;
                        case MessageContext.MESSAGE_CTX_ACTIVESYNC_WZ_STATUS:
                            s = "WizardStatusLabel = ";
                            break;
                        case MessageContext.MESSAGE_CTX_SOCKET_CONNECTING:
                        case MessageContext.MESSAGE_CTX_SOCKET_CONNECTED:
                        case MessageContext.MESSAGE_CTX_SOCKET_DISCONNECTED:
                        case MessageContext.MESSAGE_CTX_SOCKET_RESET:
                        case MessageContext.MESSAGE_CTX_SOCKET_LISTENING:
                            SocketEventType evType;

                            switch (context)
                            {
                                case MessageContext.MESSAGE_CTX_SOCKET_CONNECTING:
                                    evType = SocketEventType.CONNECTING;
                                    break;
                                case MessageContext.MESSAGE_CTX_SOCKET_CONNECTED:
                                    if (direction == PacketDirection.PACKET_DIRECTION_INCOMING)
                                        evType = SocketEventType.CONNECTED_INBOUND;
                                    else
                                        evType = SocketEventType.CONNECTED_OUTBOUND;
                                    break;
                                case MessageContext.MESSAGE_CTX_SOCKET_DISCONNECTED:
                                    evType = SocketEventType.DISCONNECTED;
                                    break;
                                case MessageContext.MESSAGE_CTX_SOCKET_RESET:
                                    evType = SocketEventType.RESET;
                                    break;
                                case MessageContext.MESSAGE_CTX_SOCKET_LISTENING:
                                    evType = SocketEventType.LISTENING;
                                    break;
                                default:
                                    evType = SocketEventType.UNKNOWN;
                                    break;
                            }

                            TCPEvent ev = new TCPEvent(timestamp, resourceId, evType, localEndpoint, remoteEndpoint);
                            tmpEventList.Add(ev);
                            
                            break;
                    }

                    if (s != "")
                        e.Row["Description"] = String.Format("{0}\"{1}\"", s, e.Row["Message"]);
                    else
                        e.Row["Description"] = e.Row["Message"];
                }
                else
                {
                    string msgPrefix = "";
                    if (localEndpoint.Address.Length > 0)
                    {
                        msgPrefix = String.Format("{0}: ", localEndpoint);
                    }

                    string msgSuffix = "";
                    if (remoteEndpoint.Address.Length > 0)
                    {
                        msgSuffix = String.Format(" {0} {1}",
                            (direction == PacketDirection.PACKET_DIRECTION_INCOMING) ? "from" : "to",
                            remoteEndpoint);
                    }

                    if (msgPrefix.Length > 0 && msgSuffix.Length > 0)
                    {
                        if (direction == PacketDirection.PACKET_DIRECTION_INCOMING)
                        {
                            e.Row["Description"] = String.Format("{0}Received {1} byte{2}{3}",
                                                                 msgPrefix, data.Length, suffix, msgSuffix);
                        }
                        else
                        {
                            e.Row["Description"] = String.Format("{0}Sent {1} byte{2}{3}",
                                                                 msgPrefix, data.Length, suffix, msgSuffix);
                        }
                    }
                    else
                    {
                        e.Row["Description"] = e.Row["Message"];
                    }

                    IPPacket pkt = new IPPacket(index, timestamp, resourceId, direction, localEndpoint, remoteEndpoint, data);
                    tmpPacketList.Add(pkt);
                }

                e.Row["BgColor"] = GetColorForASState(e.Row);

                e.Row.EndEdit();
            }
        }

        private string MD5Sum(string str)
        {
            System.Security.Cryptography.MD5CryptoServiceProvider x = new System.Security.Cryptography.MD5CryptoServiceProvider();
            byte[] bs = System.Text.Encoding.UTF8.GetBytes(str);

            bs = x.ComputeHash(bs);

            StringBuilder s = new StringBuilder();

            foreach (byte b in bs)
            {
                s.Append(b.ToString("x2").ToLower());
            }

            return s.ToString();
        }

        private byte[] GetColorForASState(DataRow row)
        {
            string dev, status, subStatus, wizStatus;
            byte[] c = new byte[3];

            dev = (string)row["AS_Device"];
            status = (string)row["AS_Status"];
            subStatus = (string)row["AS_SubStatus"];
            wizStatus = (string)row["AS_WizStatus"];

            string devHash = MD5Sum(dev);
            string statusHash = MD5Sum(status);
            string subStatusHash = MD5Sum(subStatus);
            string wizStatusHash = MD5Sum(wizStatus);

            string combinedHash = MD5Sum(devHash + statusHash + subStatusHash + wizStatusHash);

            Color col = colorPool.GetColorForId(combinedHash);

            byte[] b = new byte[3];
            b[0] = col.R;
            b[1] = col.G;
            b[2] = col.B;

            return b;
        }

        private void propertyGrid_SelectedGridItemChanged(object sender, SelectedGridItemChangedEventArgs e)
        {
            PropertyGridItemSelected(e.NewSelection);
        }

        private void PropertyGridItemSelected(GridItem item)
        {
            List<string> tokens = new List<string>(4);

            while (item != null)
            {
                tokens.Add(item.Label);
                item = item.Parent;
            }
            tokens.Reverse();

            if (tokens.Count < 3)
                return;

            int nodePos = Convert.ToInt32(tokens[2]);

            StringBuilder str = new StringBuilder(64);
            for (int i = 3; i < tokens.Count; i++)
            {
                str.AppendFormat("{0}{1}", (i > 3) ? "|" : "", tokens[i]);
            }
            
            TransactionNode node = curNodeList[nodePos];
            List<PacketSlice> slices = node.GetSlicesForFieldPath(str.ToString());

            if (autoHighlightMenuItem.Checked)
            {
                UpdateDumpHighlighting(slices.ToArray());
            }

            if (slices.Count >= 1)
            {
                ScrollPacketIntoView(slices[0]);
            }
        }

        private void ScrollPacketIntoView(PacketSlice slice)
        {
            PacketPosition pPos = (PacketPosition) slice.Packet.Tag;

            if (!pPos.Row.Displayed)
            {
                dataGridView.FirstDisplayedScrollingRowIndex = pPos.Row.Index;
            }

            int lineStart, lineEnd;
            if (dumpDisplayMode == DisplayMode.HEX)
            {
                lineStart = pPos.LineStart + (slice.Offset / 16);
                int bytesRemaining = slice.Length - (16 - (slice.Offset % 16));
                if (bytesRemaining < 0)
                    bytesRemaining = 0;
                lineEnd = lineStart + (bytesRemaining / 16);
            }
            else
            {
                int i = pPos.GetLineOffsetIndexFromPacketOffset(slice.Offset);
                if (i < 0)
                    return;

                lineStart = pPos.LineStart + i;
                lineEnd = lineStart + 1; // FIXME
            }

            System.Drawing.Graphics gfx = CreateGraphics();
            int linePixelHeight = (int) Math.Round((gfx.DpiY / 70.0f) * richTextBox.Font.SizeInPoints);

            int displayedLineFirst = StaticUtils.GetScrollPos(richTextBox.Handle, StaticUtils.SB_VERT) / linePixelHeight;
            int displayedLineLast = displayedLineFirst + (richTextBox.Height / linePixelHeight);
            if (lineStart < displayedLineFirst || lineEnd > displayedLineLast)
            {
                richTextBox.Select(richTextBox.GetFirstCharIndexFromLine(lineStart), 0);
                richTextBox.ScrollToCaret();
            }
        }

        private PacketSlice[] OptimizeSlices(PacketSlice[] slices)
        {
            if (slices == null)
                return null;

            List<PacketSlice> optimizedSlices = new List<PacketSlice>(slices.Length);

            PacketSlice newSlice = null;

            foreach (PacketSlice slice in slices)
            {
                if (newSlice == null)
                {
                    newSlice = new PacketSlice(slice.Packet, slice.Offset, slice.Length);
                }
                else
                {
                    if (slice.Packet == newSlice.Packet &&
                        slice.Offset == newSlice.Offset + newSlice.Length)
                    {
                        newSlice.Length += slice.Length;
                    }
                    else
                    {
                        optimizedSlices.Add(newSlice);
                        newSlice = new PacketSlice(slice.Packet, slice.Offset, slice.Length);
                    }
                }
            }

            if (newSlice != null)
            {
                optimizedSlices.Add(newSlice);
            }

            return optimizedSlices.ToArray();
        }

        private void UpdateDumpHighlighting(PacketSlice[] slices)
        {
            slices = OptimizeSlices(slices);

            if (prevSelectedSlices != null)
            {
                Color cl = richTextBox.BackColor;
                HighlightSlices(prevSelectedSlices, ref cl);
            }

            prevSelectedSlices = slices;

            if (slices == null)
                return;

            Color bgColor = Color.FromArgb(126, 162, 88);

            HighlightSlices(slices, ref bgColor);
        }

        private void HighlightSlices(PacketSlice[] slices, ref Color bgColor)
        {
            foreach (PacketSlice slice in slices)
            {
                int offset = slice.Offset;
                int length = slice.Length;
                int viewLineNo, basePos;

                if (slice.Packet.Tag == null)
                    return;

                PacketPosition viewPos = (PacketPosition)slice.Packet.Tag;

                // FIXME: this should be cleaned up

                if (dumpDisplayMode == DisplayMode.ASCII)
                {
                    LineOffset[] offsets = viewPos.LineOffsets;

                    int i = viewPos.GetLineOffsetIndexFromPacketOffset(offset);
                    if (i < 0)
                        return;

                    viewLineNo = viewPos.LineStart + 1 + i;
                    for (; i < offsets.Length && length > 0; i++)
                    {
                        LineOffset loff = offsets[i];

                        int lineOffset = offset - loff.Start;
                        if (lineOffset < 0)
                            lineOffset = 0;

                        int lineLeft = loff.Length - lineOffset;
                        if (lineLeft > length)
                            lineLeft = length;

                        basePos = richTextBox.GetFirstCharIndexFromLine(viewLineNo) + 3;

                        richTextBox.Select(basePos + lineOffset, lineLeft);
                        richTextBox.SelectionBackColor = bgColor;

                        viewLineNo++;
                        offset += lineLeft;
                        length -= lineLeft;
                    }
                }
                else if (dumpDisplayMode == DisplayMode.HEX)
                {
                    viewLineNo = viewPos.LineStart + 1 + (offset / 16);

                    while (length > 0)
                    {
                        int lineOffset = offset % 16;
                        int lineLeft = 16 - lineOffset;
                        if (lineLeft > length)
                            lineLeft = length;

                        basePos = richTextBox.GetFirstCharIndexFromLine(viewLineNo);

                        int rPos = basePos + 9 + (lineOffset * 3);
                        richTextBox.Select(rPos, (lineLeft * 3) - 1);
                        richTextBox.SelectionBackColor = bgColor;

                        rPos = basePos + 9 + (16 * 3) + 1 + lineOffset;
                        richTextBox.Select(rPos, lineLeft);
                        richTextBox.SelectionBackColor = bgColor;

                        viewLineNo++;
                        offset += lineLeft;
                        length -= lineLeft;
                    }
                }
            }
        }

        protected struct PacketPosition
        {
            public DataGridViewRow Row;
            public int LineStart;
            public int LineEnd;
            public LineOffset[] LineOffsets;

            public PacketPosition(DataGridViewRow row,
                                  int lineStart,
                                  int lineEnd,
                                  LineOffset[] lineOffsets)
            {
                Row = row;
                LineStart = lineStart;
                LineEnd = lineEnd;
                LineOffsets = lineOffsets;
            }

            public int GetLineOffsetIndexFromPacketOffset(int offset)
            {
                for (int i = LineOffsets.Length - 1; i >= 0; i--)
                {
                    if (offset >= LineOffsets[i].Start)
                        return i;
                }

                return -1;
            }
        }

        public struct LineOffset
        {
            public int Start;
            public int Length;
            public int TermLen;

            public LineOffset(int start, int length, int termLen)
            {
                Start = start;
                Length = length;
                TermLen = termLen;
            }
        }

        private void dataGridView_SelectionChanged(object sender, EventArgs e)
        {
            if (updatingSelections)
                return;

            UpdateDumpView();
        }

        // FIXME: a really naive implementation done in a hurry
        private void richTextBox_SelectionChanged(object sender, EventArgs e)
        {
#if false
            curSelBytes = new MemoryStream();
            statusBarLabel.Text = "";

            int selLen = richTextBox.SelectionLength;
            if (selLen == 0)
            {
                return;
            }

            int startGlobalOff = richTextBox.SelectionStart;

            int off = startGlobalOff;
            int line = richTextBox.GetLineFromCharIndex(startGlobalOff);
            int lineOff = startGlobalOff - richTextBox.GetFirstCharIndexFromLine(line);

            int hexBytesPerLine = 16;
            int hexLeftMarginLength = 9;
            int hexRightMarginLength = 2;
            int hexAsciiBorder = hexLeftMarginLength + ((hexBytesPerLine * 3) - 1) + (hexRightMarginLength / 2);
            int hexAsciiStart = hexAsciiBorder + (hexRightMarginLength / 2);
            int hexLineLength = hexAsciiStart + hexBytesPerLine + 1;

            int asciiLeftMarginLength = 3;

            int delta;

            int charsStart = -1;
            int charsPerByte = -1;
            int charSpacing = -1;

            while (selLen > 0)
            {
                // Which packet are we in?
                IPPacket pkt = null;
                if (curLinesToPackets.ContainsKey(line))
                {
                    pkt = curLinesToPackets[line];
                }
                else
                {
                    delta = richTextBox.GetFirstCharIndexFromLine(line + 1) - off;
                    if (delta == 0)
                        break;

                    selLen -= delta;
                    off += delta;
                    line++;
                    lineOff = 0;

                    continue;
                }

                // How far into the packet are we?
                PacketPosition pktPos = (PacketPosition) pkt.Tag;
                int pktLinesSkipped = line - pktPos.LineStart - 1;
                int pktOff = -1, pktSelLen = -1, lineLen = -1;

                if (charsPerByte == -1)
                {
                    if (dumpDisplayMode == DisplayMode.HEX)
                    {
                        // First iteration: does the selection start in the hex or in the ASCII area?
                        if (lineOff < hexAsciiBorder)
                        {
                            charsStart = hexLeftMarginLength;
                            charsPerByte = 2;
                            charSpacing = 1;
                        }
                        else
                        {
                            charsStart = hexAsciiStart;
                            charsPerByte = 1;
                            charSpacing = 0;
                        }
                    }
                    else
                    {
                        charsStart = asciiLeftMarginLength;
                        charsPerByte = 1;
                        charSpacing = 0;
                    }
                }

                // Skip any irrelevant characters
                if (lineOff < charsStart)
                {
                    delta = charsStart - lineOff;

                    selLen -= delta;
                    off += delta;
                    lineOff += delta;

                    if (selLen <= 0)
                        break;
                }

                if (dumpDisplayMode == DisplayMode.HEX)
                {
                    pktOff = pktLinesSkipped * hexBytesPerLine;

                    // What is the byte offset on this line?
                    int lineCharsOffset = lineOff - charsStart;
                    int byteCharsWidth = charsPerByte + charSpacing;

                    int byteOff = lineCharsOffset / byteCharsWidth;
                    if (lineCharsOffset % byteCharsWidth == charsPerByte)
                        byteOff += charSpacing;

                    pktOff += byteOff;

                    // How many bytes are selected on the current line?
                    int lineCharsSel = Math.Min(selLen, (hexBytesPerLine - byteOff) * byteCharsWidth);

                    pktSelLen = lineCharsSel / byteCharsWidth;
                    if (lineCharsSel % byteCharsWidth == charsPerByte)
                        pktSelLen += charSpacing;

                    lineLen = hexLineLength;
                }
                else
                {
                    LineOffset lo = pktPos.LineOffsets[pktLinesSkipped];
                    pktOff = lo.Start + (lineOff - charsStart);
                    pktSelLen = Math.Min(selLen, lo.Length);
                    if (selLen > lo.Length)
                        pktSelLen += lo.TermLen;
                    lineLen = asciiLeftMarginLength + lo.Length;
                    if (lo.TermLen > 0)
                        lineLen++;
                }

                // Store them
                if (pktOff < pkt.Bytes.Length)
                {
                    curSelBytes.Write(pkt.Bytes, pktOff, Math.Min(pktSelLen, pkt.Bytes.Length - pktOff));
                }

                // Update the state
                delta = lineLen - lineOff;

                selLen -= delta;
                off += delta;
                line++;
                lineOff = 0;
            }

            if (curSelBytes.Length > 0)
            {
                statusBarLabel.Text = String.Format("{0} byte{1} selected",
                    curSelBytes.Length, (curSelBytes.Length > 1) ? "s" : "");
            }
#endif
        }

        private void UpdateDumpView()
        {
            StringBuilder builder = new StringBuilder();

            foreach (DataGridViewRow row in dataGridView.SelectedRows)
            {
                DumpEvent ev = row.Cells[eventTextCol.Index].Value as DumpEvent;
                builder.Append(ev.Data);
                builder.Append("\n\n");
            }

            richTextBox.Text = builder.ToString();
        }

#if false
        private void UpdateDumpView()
        {
            //
            // Sort the selected packets
            //
            List<IPPacket> packets = new List<IPPacket>();
            Dictionary<IPPacket, DataGridViewRow> packetToRow = new Dictionary<IPPacket, DataGridViewRow>();
            foreach (DataGridViewRow row in dataGridView.SelectedRows)
            {
                if ((UInt32)row.Cells[msgTypeDataGridViewTextBoxColumn.Index].Value != Convert.ToUInt32(MessageType.MESSAGE_TYPE_PACKET))
                    continue;

                int index = (int)row.Cells[indexDataGridViewTextBoxColumn.Index].Value;
                IPPacket packet = packetParser.GetPacket(index);
                packets.Add(packet);
                packetToRow[packet] = row;
            }
            packets.Sort();

            curLinesToPackets = new Dictionary<int, IPPacket>(packets.Count);
            curPacketList = packets;

            //
            // Iterate through the selected packets and add a raw dump
            // of each, and add each to the list of decoded packets
            //
            StringBuilder dump = new StringBuilder(512);

            int packetNo = 0;
            int lineNo = 0;

            string[] lines;

            foreach (IPPacket packet in packets)
            {
                if (packetNo > 0)
                {
                    dump.Append("\n");
                    lineNo++;
                }

                int lineStart = lineNo;

                string linePrefix;
                switch (packet.Direction)
                {
                    case PacketDirection.PACKET_DIRECTION_INCOMING:
                        linePrefix = "<< ";
                        break;
                    case PacketDirection.PACKET_DIRECTION_OUTGOING:
                        linePrefix = ">> ";
                        break;
                    default:
                        linePrefix = ">< ";
                        break;
                }

                dump.AppendFormat("{0}.��.� #{1}\n", linePrefix, packet.Index);
                lineNo++;

                int nLines = 0;
                LineOffset[] lineOffsets = new LineOffset[0];
                if (dumpDisplayMode == DisplayMode.HEX)
                {
                    dump.Append(StaticUtils.ByteArrayToHexDump(packet.Bytes, linePrefix, out nLines));
                    dump.Append("\n");
                }
                else
                {
                    string str = StaticUtils.DecodeASCII(packet.Bytes);

                    /*
                    if (str.EndsWith("\r\n"))
                    {
                        str = str.Substring(0, str.Length - 2);
                    }
                    else if (str.EndsWith("\n"))
                    {
                        str = str.Substring(0, str.Length - 1);
                    }*/

                    List<LineOffset> tmpList = new List<LineOffset>(1);

                    int offset = 0;
                    do
                    {
                        int p1 = str.IndexOf("\r\n", offset);
                        int p2 = str.IndexOf("\n", offset);

                        int pos;
                        if (p1 >= 0 && p2 >= 0)
                        {
                            pos = Math.Min(p1, p2);
                        }
                        else
                        {
                            pos = (p1 >= 0) ? p1 : p2;
                        }

                        int termLen = (pos == p1) ? 2 : 1;

                        if (pos == -1)
                        {
                            pos = str.Length;
                            termLen = 0;
                        }

                        int length = pos - offset;
                        string line = str.Substring(offset, length);
                        tmpList.Add(new LineOffset(offset, length, termLen));

                        dump.AppendFormat("{0}{1}\n", linePrefix, line);
                        nLines++;

                        offset += pos - offset + termLen;
                    } while (offset < str.Length);

                    lineOffsets = tmpList.ToArray();
                }

                for (int i = 0; i < nLines; i++)
                {
                    curLinesToPackets[lineNo + i] = packet;
                }

                lineNo += nLines;

                packet.Tag = new PacketPosition(packetToRow[packet], lineStart, lineNo, lineOffsets);

                packetNo++;
            }

            richTextBox.Text = dump.ToString();

            richTextBox.SelectAll();
            richTextBox.SelectionColor = richTextBox.ForeColor;
            richTextBox.SelectionBackColor = richTextBox.BackColor;

            prevSelectedSlices = null;

            lines = richTextBox.Lines;
            Color colorIncoming = Color.FromArgb(138, 232, 153);
            Color colorOutgoing = Color.FromArgb(156, 183, 209);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("<<"))
                {
                    richTextBox.Select(richTextBox.GetFirstCharIndexFromLine(i), 2);
                    richTextBox.SelectionColor = colorIncoming;
                }
                else if (lines[i].StartsWith(">>"))
                {
                    richTextBox.Select(richTextBox.GetFirstCharIndexFromLine(i), 2);
                    richTextBox.SelectionColor = colorOutgoing;
                }
                else if (lines[i].StartsWith("><"))
                {
                    int index = richTextBox.GetFirstCharIndexFromLine(i);

                    richTextBox.Select(index, 1);
                    richTextBox.SelectionColor = colorIncoming;
                    richTextBox.Select(index + 1, 1);
                    richTextBox.SelectionColor = colorOutgoing;
                }
            }

            curNodeList = packetParser.GetTransactionsForPackets(packets);
            propertyGrid.SelectedObject = curNodeList;

            if (curNodeList.Count > 0)
            {
                UpdateDumpHighlighting(curNodeList[0].GetAllSlices().ToArray());
            }
        }
#endif

        private void dataGridView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
        }

#if false
        private void dataGridView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (dataGridView.Rows.GetRowCount(DataGridViewElementStates.Selected) != 1)
                return;

            DataGridViewRow selRow = dataGridView.SelectedRows[0];
            Rectangle rectRow = dataGridView.GetRowDisplayRectangle(selRow.Index, true);
            Rectangle rectMouse = new Rectangle(e.X, e.Y, 1, 1);
            if (rectRow.IntersectsWith(rectMouse))
            {
                UInt32 selResId;
                string selLocalAddr, selPeerAddr;
                UInt16 selLocalPort, selPeerPort;
                GetAddressesFromGridViewRow(selRow, out selResId, out selLocalAddr, out selLocalPort, out selPeerAddr, out selPeerPort);

                updatingSelections = true;

                foreach (DataGridViewRow row in dataGridView.Rows)
                {
                    UInt32 resId;
                    string localAddr, peerAddr;
                    UInt16 localPort, peerPort;
                    GetAddressesFromGridViewRow(row, out resId, out localAddr, out localPort, out peerAddr, out peerPort);

                    if (resId == 0 || selResId == 0)
                    {
                        if (peerPort != selPeerPort || localPort != selLocalPort)
                            continue;

                        if (peerAddr != selPeerAddr || localAddr != selLocalAddr)
                            continue;
                    }
                    else
                    {
                        if (resId != selResId)
                            continue;

                        if (peerPort != selPeerPort)
                            continue;
                    }

                    row.Selected = true;
                }

                // Trigger the changed event
                selRow.Selected = false;
                updatingSelections = false;
                selRow.Selected = true;
            }
        }

        private void GetAddressesFromGridViewRow(DataGridViewRow row,
                                                 out UInt32 resourceId,
                                                 out string localAddr,
                                                 out UInt16 localPort,
                                                 out string peerAddr,
                                                 out UInt16 peerPort)
        {
            DataGridViewCellCollection cells = row.Cells;

            resourceId = (UInt32)cells[resourceIdDataGridViewTextBoxColumn.Index].Value;

            localAddr = (string)cells[localAddressDataGridViewTextBoxColumn.Index].Value;
            localPort = (UInt16)cells[localPortDataGridViewTextBoxColumn.Index].Value;

            peerAddr = (string)cells[peerAddressDataGridViewTextBoxColumn.Index].Value;
            peerPort = (UInt16)cells[peerPortDataGridViewTextBoxColumn.Index].Value;
        }
#endif

        private void expandAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            propertyGrid.ExpandAllGridItems();
        }

        private void collapseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            propertyGrid.CollapseAllGridItems();
        }

        private void autoHighlightMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (autoHighlightMenuItem.Checked)
            {
                PropertyGridItemSelected(propertyGrid.SelectedGridItem);
            }
        }

        private void saveRawDumpToFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dumpSaveFileDialog.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(dumpSaveFileDialog.FileName, FileMode.Create);

                foreach (IPPacket packet in curPacketList)
                {
                    fs.Write(packet.Bytes, 0, packet.Bytes.Length);
                }

                fs.Close();
            }
        }

        private void showMSNP2PConversationsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ConversationsForm frm = new ConversationsForm(packetParser.GetSessions());
            frm.Show(this);
        }

        private void debugToolStripMenuItem_Click(object sender, EventArgs e)
        {
            debugForm.Show();
        }

        private void goToReturnaddressInIDAToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if false
            DataGridViewRow row = dataGridView.SelectedRows[0];

            string moduleName = (string)row.Cells[callerModuleNameDataGridViewTextBoxColumn.Index].Value;
            UInt32 returnAddress = (UInt32)row.Cells[returnAddressDataGridViewTextBoxColumn.Index].Value;

            Util.IDA.GoToAddressInIDA(moduleName, returnAddress);
#endif
        }

        private void showbacktraceToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if false
            DataGridViewRow row = dataGridView.SelectedRows[0];
            Int32 index = (Int32)row.Cells[indexDataGridViewTextBoxColumn.Index].Value;
            string functionName = (string)row.Cells[functionNameDataGridViewTextBoxColumn.Index].Value;
            string backtrace = (string)row.Cells[backtraceDataGridViewTextBoxColumn.Index].Value;

            BacktraceForm btForm = new BacktraceForm(index, functionName, backtrace);
            btForm.Show(this);
#endif
        }

        private void createSwRuleFromEntryToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if false
            DataGridViewRow row = dataGridView.SelectedRows[0];

            string processName = (string)row.Cells[processNameDataGridViewTextBoxColumn.Index].Value;
            string functionName = (string)row.Cells[functionNameDataGridViewTextBoxColumn.Index].Value;

            object obj;

            UInt32 returnAddress = 0;
            obj = row.Cells[returnAddressDataGridViewTextBoxColumn.Index].Value;
            if (obj is UInt32)
                returnAddress = (UInt32) obj;

            string localAddress = null;
            obj = row.Cells[localAddressDataGridViewTextBoxColumn.Index].Value;
            if (obj is string)
                localAddress = (string)obj;

            UInt16 localPort = 0;
            obj = row.Cells[localPortDataGridViewTextBoxColumn.Index].Value;
            if (obj is UInt16)
                localPort = (UInt16)obj;

            string remoteAddress = null;
            obj = row.Cells[peerAddressDataGridViewTextBoxColumn.Index].Value;
            if (obj is string)
                remoteAddress = (string)obj;

            UInt16 remotePort = 0;
            obj = row.Cells[peerPortDataGridViewTextBoxColumn.Index].Value;
            if (obj is UInt16)
                remotePort = (UInt16)obj;

            swForm.AddRule(processName, functionName, returnAddress, localAddress, localPort,
                           remoteAddress, remotePort);
            swForm.ShowDialog();
#endif
        }

        private void dataGridContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
#if false
            bool selected = (dataGridView.SelectedRows.Count > 0);

            goToReturnaddressInIDAToolStripMenuItem.Enabled = selected;

            showbacktraceToolStripMenuItem.Enabled = selected;
            if (selected)
            {
                string bt = (string) dataGridView.SelectedRows[0].Cells[backtraceDataGridViewTextBoxColumn.Index].Value;
                if (bt.Length == 0)
                    showbacktraceToolStripMenuItem.Enabled = false;
            }

            createSwRuleFromEntryToolStripMenuItem.Enabled = selected;
#endif
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBoxForm frm = new AboutBoxForm();
            frm.ShowDialog();
        }

        private void manageSoftwallRulesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            swForm.ShowDialog();
        }

        private void aSCIIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            aSCIIToolStripMenuItem.Checked = true;
            hexToolStripMenuItem.Checked = false;

            DumpDisplayMode = DisplayMode.ASCII;
        }

        private void hexToolStripMenuItem_Click(object sender, EventArgs e)
        {
            aSCIIToolStripMenuItem.Checked = false;
            hexToolStripMenuItem.Checked = true;

            DumpDisplayMode = DisplayMode.HEX;
        }

        private void filterComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyFilters();
                dataGridView.Focus();
            }
        }

        private void filterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyFilters();
            dataGridView.Focus();
        }

        private void ApplyFilters()
        {
            StringBuilder builder = new StringBuilder();

            if (!viewInternalDebugToolStripMenuItem.Checked)
            {
                builder.AppendFormat("(NOT FunctionName LIKE '*Debug*')");
            }

            if (!viewWinCryptToolStripMenuItem.Checked)
            {
                if (builder.Length > 0)
                    builder.Append(" AND ");

                builder.AppendFormat("(NOT FunctionName LIKE 'Crypt*')");
            }

            if (filterComboBox.Text.Length > 0)
            {
                if (builder.Length > 0)
                    builder.Append(" AND ");

                builder.Append(filterComboBox.Text);
            }

            try
            {
                bindingSource.Filter = builder.ToString();
            }
            catch (SyntaxErrorException ex)
            {
                MessageBox.Show(String.Format("Syntax error: {0}", ex.Message), "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (EvaluateException ex)
            {
                MessageBox.Show(String.Format("Error while evaluation expression: {0}\n\n" +
                    "For information about all available column names, save the capture " +
                    "to a file (.osd), uncompress it with bunzip2 and have a look at " +
                    "the result with a text editor.", ex.Message),
                    "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private enum FindDataType
        {
            ASCII = 0,
            ASCII_CASE_INSENSITIVE,
            UTF16LE,
            UTF16LE_CASE_INSENSITIVE,
            RAW_HEX,
        }

        private void findComboBox_KeyDown(object sender, KeyEventArgs e)
        {
#if false
            if (e.KeyCode != Keys.Enter)
                return;

            FindDataType type = (FindDataType) findTypeComboBox.SelectedIndex;

            updatingSelections = true;

            string needle = findComboBox.Text;

            int nMatches = 0;

            if (type == FindDataType.RAW_HEX)
            {
                needle = needle.Replace(" ", "");
                if (needle.Length % 2 != 0)
                    throw new FormatException("length not divisible with two");

                List<byte> bytes = new List<byte>(1);
                for (int i = 0; i < needle.Length; i += 2)
                {
                    bytes.Add(Convert.ToByte(needle.Substring(i, 2), 16));
                }

                foreach (DataGridViewRow row in dataGridView.Rows)
                {
                    bool selected = false;

                    byte[] data = row.Cells[dataDataGridViewImageColumn.Index].Value as byte[];
                    if (data != null)
                    {
                        for (int i = 0; i < data.Length - bytes.Count; i++)
                        {
                            selected = true;

                            for (int j = 0; j < bytes.Count; j++)
                            {
                                if (data[i + j] != bytes[j])
                                {
                                    selected = false;
                                    break;
                                }
                            }

                            if (selected)
                                break;
                        }
                    }

                    row.Selected = selected;

                    if (selected)
                        nMatches++;
                }
            }
            else
            {
                Decoder dec;

                if (type == FindDataType.ASCII ||
                    type == FindDataType.ASCII_CASE_INSENSITIVE)
                {
                    dec = Encoding.ASCII.GetDecoder();
                }
                else
                {
                    dec = Encoding.Unicode.GetDecoder();
                }

                bool noCase = (type == FindDataType.ASCII_CASE_INSENSITIVE ||
                               type == FindDataType.UTF16LE_CASE_INSENSITIVE);

                if (noCase)
                    needle = needle.ToLower();

                foreach (DataGridViewRow row in dataGridView.Rows)
                {
                    bool selected = false;

                    byte[] data = row.Cells[dataDataGridViewImageColumn.Index].Value as byte[];
                    if (data != null)
                    {
                        char[] chars = new char[dec.GetCharCount(data, 0, data.Length)];
                        dec.GetChars(data, 0, data.Length, chars, 0);
                        string str = new string(chars);
                        if (noCase)
                            str = str.ToLower();

                        selected = str.Contains(needle);
                    }

                    row.Selected = selected;

                    if (selected)
                        nMatches++;
                }
            }

            updatingSelections = false;

            UpdateDumpView();

            statusBarLabel.Text = String.Format("{0} match{1} found",
                (nMatches > 0) ? Convert.ToString(nMatches) : "No",
                (nMatches == 0 || nMatches > 1) ? "es" : "");
#endif
        }

        private void findTypeComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            richTextBox.Copy();
        }

        private void copyRawToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // FIXME: figure out how to copy raw data to the clipboard
            byte[] bytes = curSelBytes.ToArray();
            Clipboard.SetText(StaticUtils.DecodeASCII(bytes));
        }

        private void selSaveToFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dumpSaveFileDialog.ShowDialog() == DialogResult.OK)
            {
                FileStream fs = new FileStream(dumpSaveFileDialog.FileName, FileMode.Create);

                byte[] bytes = curSelBytes.ToArray();
                fs.Write(bytes, 0, bytes.Length);

                fs.Close();
            }
        }

        private void findComboBox_Leave(object sender, EventArgs e)
        {
            statusBarLabel.Text = "";
        }

        private void parserConfigToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Configuration.ParserConfigDialog pcDialog = new oSpy.Configuration.ParserConfigDialog(packetParser);
            if (pcDialog.ShowDialog() == DialogResult.OK) {
            
            }
        }

        private void rowsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            dataGridView.SelectAll();
        }

        private void UnselectAllRows()
        {
            // Gotta be a more elegant way to do this...
            foreach (DataGridViewRow row in dataGridView.SelectedRows)
            {
                row.Selected = false;
            }
        }

        private void transactionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if false
            updatingSelections = true;

            UnselectAllRows();

            // Select the visible rows with these indexes
            Dictionary<int, List<TransactionNode>> allIndexes = packetParser.GetAllTransactionPacketIndexes();

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                int index = (int) row.Cells[indexDataGridViewTextBoxColumn.Index].Value;

                if (allIndexes.ContainsKey(index))
                {
                    row.Selected = true;
                }
            }

            // Refresh
            updatingSelections = false;
            DataGridViewRow selRow = null;

            if (dataGridView.SelectedRows.Count > 0)
            {
                selRow = dataGridView.SelectedRows[0];
            }
            else if (dataGridView.Rows.Count > 0)
            {
                selRow = dataGridView.Rows[0];
            }

            if (selRow != null)
            {
                selRow.Selected = false;
                selRow.Selected = true;
            }
#endif
        }

        private bool GetNextRowIndex(out int index)
        {
            index = -1;

            if (dataGridView.Rows.Count == 0)
                return false;

            if (dataGridView.SelectedRows.Count >= 1)
                index = dataGridView.SelectedRows[0].Index + 1;
            else
                index = dataGridView.Rows[0].Index;

            return true;
        }

        private void GoToNextPacket()
        {
#if false
            int startIndex;
            if (!GetNextRowIndex(out startIndex))
                return;

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (row.Index >= startIndex)
                {
                    MessageType msgType = (MessageType)((UInt32)row.Cells[msgTypeDataGridViewTextBoxColumn.Index].Value);
                    PacketDirection direction = (PacketDirection)((UInt32)row.Cells[directionDataGridViewTextBoxColumn.Index].Value);

                    if (msgType == MessageType.MESSAGE_TYPE_PACKET && direction != PacketDirection.PACKET_DIRECTION_INVALID)
                    {
                        dataGridView.CurrentCell = dataGridView[0, row.Index];
                        break;
                    }
                }
            }
#endif
        }

        private void GoToNextTransactionRow()
        {
#if false
            int startIndex;
            if (!GetNextRowIndex(out startIndex))
                return;

            Dictionary<int, List<TransactionNode>> allIndexes = packetParser.GetAllTransactionPacketIndexes();

            foreach (DataGridViewRow row in dataGridView.Rows)
            {
                if (row.Index >= startIndex)
                {
                    int msgIndex = (int)row.Cells[indexDataGridViewTextBoxColumn.Index].Value;

                    if (allIndexes.ContainsKey(msgIndex))
                    {
                        dataGridView.CurrentCell = dataGridView[0, row.Index];
                        break;
                    }
                }
            }
#endif
        }

        private void nextpacketToolStripMenuItem_Click(object sender, EventArgs e)
        {
            GoToNextPacket();
        }

        private void nextRowTransactionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            GoToNextTransactionRow();
        }

        private void nextPacketBtn_Click(object sender, EventArgs e)
        {
            GoToNextPacket();
        }

        private void nextTransactionBtn_Click(object sender, EventArgs e)
        {
            GoToNextTransactionRow();
        }

        private void viewInternalDebugToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            ApplyFilters();
        }

        private void viewWinCryptToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            ApplyFilters();
        }

        private void applydebugSymbolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string appDir =
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName);

            Dictionary<string, DebugSymbols> symbols = new Dictionary<string, DebugSymbols>();
            foreach (string filename in Directory.GetFiles(appDir, "*.osym"))
            {
                string path = appDir + "\\" + filename;

                DebugSymbols sym;

                try
                {
                    sym = new DebugSymbols(filename);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(String.Format("Failed to load {0}: {1}", filename, ex.Message),
                                                  "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    continue;
                }

                symbols[sym.Filename.ToLower()] = sym;
            }

            if (symbols.Count == 0)
            {
                MessageBox.Show("No symbols found. Place one or more .osym files in the application directory.",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            char[] lineSplitChars = new char[] { '\n' };
            string[] btSepStrs = new string[] { "::" };
            char[] btSubSepChars = new char[] { ' ' };

            foreach (DataRow row in dataSet.Tables[0].Rows)
            {
                string bt = (string) row["Backtrace"];
                if (bt == String.Empty)
                    continue;

                StringBuilder builder = new StringBuilder(bt.Length);

                int i = 0;
                foreach (string line in bt.Split(lineSplitChars))
                {
                    string[] tokens = line.Split(btSepStrs, 2, StringSplitOptions.None);
                    string moduleName = tokens[0].ToLower();
                    string[] subTokens = tokens[1].Split(btSubSepChars, 2);

                    string funcName = String.Empty;
                    if (subTokens.Length == 2)
                        funcName = subTokens[1].Substring(1, subTokens[1].Length - 2);

                    if (symbols.ContainsKey(moduleName))
                    {
                        uint offset = Convert.ToUInt32(subTokens[0].Substring(2), 16);

                        FunctionSymbol sym = symbols[moduleName].FindFunction(offset);
                        if (sym != null)
                        {
                            funcName = String.Format("{0}+0x{1:x}", sym.Name, offset - sym.Start);
                        }
                    }

                    if (i > 0)
                        builder.Append("\n");

                    string suffix = "";
                    if (funcName != String.Empty)
                        suffix = String.Format(" ({0})", funcName);

                    builder.AppendFormat("{0}::{1:x}{2}", tokens[0], subTokens[0], suffix);

                    i++;
                }

                row["Backtrace"] = builder.ToString();
            }

            dataGridView.Refresh();

            MessageBox.Show("Symbols applied successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void dumpContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            selBytesToolStripMenuItem.Enabled = (curSelBytes.Length > 0);
        }

        private void tryggveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            oSpy.Capture.Converter conv = new oSpy.Capture.Converter();
            ProgressForm prog = new ProgressForm("Woot");
            conv.ConvertAll("C:\\Users\\Ole Andr�\\AppData\\Local\\Temp\\gatqxqhw.wvc", 206, prog);
            prog.ShowDialog();
        }
    }

    public class DebugSymbols
    {
        protected string filename;
        public string Filename
        {
            get { return filename; }
        }

        protected List<FunctionSymbol> functionSyms;

        public DebugSymbols(string path)
        {
            functionSyms = new List<FunctionSymbol>();

            TextReader reader = new StreamReader(path);

            int i = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (i > 0)
                {
                    string[] tokens = line.Split(new char[] { ';' });
                    functionSyms.Add(new FunctionSymbol(tokens[2],
                                                        Convert.ToUInt32(tokens[0].Substring(2), 16),
                                                        Convert.ToUInt32(tokens[1].Substring(2), 16)));
                }
                else
                {
                    filename = line;
                }

                i++;
            }
        }

        public FunctionSymbol FindFunction(uint offset)
        {
            // Safe slow and stupid for now

            foreach (FunctionSymbol sym in functionSyms)
            {
                if (offset >= sym.Start && offset <= sym.End)
                {
                    return sym;
                }
            }

            return null;
        }
    }

    public class FunctionSymbol
    {
        protected string name;
        public string Name
        {
            get { return name; }
        }

        protected uint start;
        public uint Start
        {
            get { return start; }
        }

        protected uint end;
        public uint End
        {
            get { return end; }
        }

        public FunctionSymbol(string name, uint start, uint end)
        {
            this.name = name;
            this.start = start;
            this.end = end;
        }
    }
}
