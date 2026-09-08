using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Runtime.InteropServices;
using System.Threading;
using EventHook.Hooks.Library;
using Microsoft.Win32.SafeHandles;

namespace EventHook.Hooks
{
    /// <summary>
    ///     http://www.codeproject.com/Articles/51085/Monitor-jobs-in-a-printer-queue-NET
    /// </summary>
    internal class PrintJobChangeEventArgs : EventArgs
    {
        internal PrintJobChangeEventArgs(int intJobID, string strJobName, JOBSTATUS jStatus,
            PrintSystemJobInfo objJobInfo)
        {
            JobId = intJobID;
            JobName = strJobName;
            JobStatus = jStatus;
            JobInfo = objJobInfo;
        }

        internal int JobId { get; }

        internal string JobName { get; }

        internal JOBSTATUS JobStatus { get; }

        internal PrintSystemJobInfo JobInfo { get; }
    }

    internal delegate void PrintJobStatusChanged(object sender, PrintJobChangeEventArgs e);

    internal class PrintQueueHook
    {
        private readonly ManualResetEvent _mrEvent = new ManualResetEvent(false);
        private readonly PRINTER_NOTIFY_OPTIONS _notifyOptions = new PRINTER_NOTIFY_OPTIONS();
        private readonly Dictionary<int, string> _objJobDict = new Dictionary<int, string>();
        private IntPtr _changeHandle = IntPtr.Zero;


        private IntPtr _printerHandle = IntPtr.Zero;
        private PrintQueue _spooler;
        private RegisteredWaitHandle _waitHandle;
        internal string SpoolerName;

        internal PrintQueueHook(string strSpoolName)
        {
            // Let us open the printer and get the printer handle.
            SpoolerName = strSpoolName;
        }

        internal event PrintJobStatusChanged OnJobStatusChange;

        ~PrintQueueHook()
        {
            Stop();
        }


        internal void Start()
        {
            OpenPrinter(SpoolerName, out _printerHandle, 0);

            if (_printerHandle != IntPtr.Zero)
            {
                //We got a valid Printer handle.  Let us register for change notification....
                _changeHandle = FindFirstPrinterChangeNotification(_printerHandle,
                    (int)PRINTER_CHANGES.PRINTER_CHANGE_JOB, 0, _notifyOptions);
                // We have successfully registered for change notification.  Let us capture the handle...
                _mrEvent.SafeWaitHandle = new SafeWaitHandle(_changeHandle, true);

                //Now, let us wait for change notification from the printer queue....
                _waitHandle = ThreadPool.RegisterWaitForSingleObject(_mrEvent, PrinterNotifyWaitCallback, _mrEvent, -1,
                    true);
            }

            _spooler = new PrintQueue(new PrintServer(), SpoolerName);
            foreach (var psi in _spooler.GetPrintJobInfoCollection())
            {
                _objJobDict[psi.JobIdentifier] = psi.Name;
            }
        }

        internal void Stop()
        {
            try
            {
                _waitHandle?.Unregister(null);
                _waitHandle = null;
                if (_printerHandle != IntPtr.Zero)
                {
                    ClosePrinter(_printerHandle);
                    _printerHandle = IntPtr.Zero;
                }
            }
            catch (Exception)
            {
                // Best-effort teardown when the spooler handle is already invalid.
            }
        }

        internal void PrinterNotifyWaitCallback(object state, bool timedOut)
        {
            if (_printerHandle == IntPtr.Zero)
            {
                return;
            }


            _notifyOptions.Count = 1;
            int pdwChange = 0;
            var pNotifyInfo = IntPtr.Zero;
            bool bResult = FindNextPrinterChangeNotification(_changeHandle, out pdwChange, _notifyOptions,
                ref pNotifyInfo);
            //If the Printer Change Notification Call did not give data, exit code
            if (!bResult || pNotifyInfo == IntPtr.Zero)
            {
                return;
            }

            //If the Change Notification was not relgated to job, exit code
            bool bJobRelatedChange = (pdwChange & PRINTER_CHANGES.PRINTER_CHANGE_ADD_JOB) ==
                                     PRINTER_CHANGES.PRINTER_CHANGE_ADD_JOB ||
                                     (pdwChange & PRINTER_CHANGES.PRINTER_CHANGE_SET_JOB) ==
                                     PRINTER_CHANGES.PRINTER_CHANGE_SET_JOB ||
                                     (pdwChange & PRINTER_CHANGES.PRINTER_CHANGE_DELETE_JOB) ==
                                     PRINTER_CHANGES.PRINTER_CHANGE_DELETE_JOB ||
                                     (pdwChange & PRINTER_CHANGES.PRINTER_CHANGE_WRITE_JOB) ==
                                     PRINTER_CHANGES.PRINTER_CHANGE_WRITE_JOB;

            if (!bJobRelatedChange)
            {
                return;
            }

            //Now, let us initialize and populate the Notify Info data
            var info = (PRINTER_NOTIFY_INFO)Marshal.PtrToStructure(pNotifyInfo, typeof(PRINTER_NOTIFY_INFO));
            long pData = (long)pNotifyInfo + (long)Marshal.OffsetOf(typeof(PRINTER_NOTIFY_INFO), "aData");
            var data = new PRINTER_NOTIFY_INFO_DATA[info.Count];
            for (uint i = 0; i < info.Count; i++)
            {
                data[i] =
                    (PRINTER_NOTIFY_INFO_DATA)Marshal.PtrToStructure((IntPtr)pData, typeof(PRINTER_NOTIFY_INFO_DATA));
                pData += Marshal.SizeOf(typeof(PRINTER_NOTIFY_INFO_DATA));
            }


            for (int i = 0; i < data.Count(); i++)
            {
                if (data[i].Field == (ushort)PRINTERJOBNOTIFICATIONTYPES.JOB_NOTIFY_FIELD_STATUS &&
                    data[i].Type == (ushort)PRINTERNOTIFICATIONTYPES.JOB_NOTIFY_TYPE
                )
                {
                    var jStatus = (JOBSTATUS)Enum.Parse(typeof(JOBSTATUS), data[i].NotifyData.Data.cbBuf.ToString());
                    int intJobId = (int)data[i].Id;
                    string strJobName;
                    PrintSystemJobInfo pji = null;
                    try
                    {
                        _spooler = new PrintQueue(new PrintServer(), SpoolerName);
                        pji = _spooler.GetJob(intJobId);
                        if (!_objJobDict.ContainsKey(intJobId))
                        {
                            _objJobDict[intJobId] = pji.Name;
                        }

                        strJobName = pji.Name;
                    }
                    catch
                    {
                        pji = null;
                        _objJobDict.TryGetValue(intJobId, out strJobName);
                        if (strJobName == null)
                        {
                            strJobName = string.Empty;
                        }
                    }

                    if (OnJobStatusChange != null)
                    {
                        //Let us raise the event
                        OnJobStatusChange(this, new PrintJobChangeEventArgs(intJobId, strJobName, jStatus, pji));
                    }
                }
            }


            _mrEvent.Reset();
            _waitHandle =
                ThreadPool.RegisterWaitForSingleObject(_mrEvent, PrinterNotifyWaitCallback, _mrEvent, -1, true);
        }


        [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi,
            ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool OpenPrinter(string pPrinterName,
            out IntPtr phPrinter,
            int pDefault);


        [DllImport("winspool.drv", EntryPoint = "ClosePrinter",
            SetLastError = true,
            ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        internal static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv",
            EntryPoint = "FindFirstPrinterChangeNotification",
            SetLastError = true, CharSet = CharSet.Ansi,
            ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr FindFirstPrinterChangeNotification
        ([In] IntPtr hPrinter,
            [In] int fwFlags,
            [In] int fwOptions,
            [In] [MarshalAs(UnmanagedType.LPStruct)]
            PRINTER_NOTIFY_OPTIONS pPrinterNotifyOptions);


        [DllImport("winspool.drv", EntryPoint = "FindNextPrinterChangeNotification",
            CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindNextPrinterChangeNotification([In] IntPtr hChange,
            [Out] out int pdwChange,
            [In] PRINTER_NOTIFY_OPTIONS
                pPrinterNotifyOptions,
            ref IntPtr ppPrinterNotifyInfo);
    }
}
