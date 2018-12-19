using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Security;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
namespace Edge
{
    public class SendData
    {
        public string label;
        public string data;
    };
    class Edge
    {
        public static string PATHMSGREC = @".\Private$\MSGHIO1Edge" + System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        public static string PATHMSGSEND = @".\Private$\MSGHIO2" + System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\').Last();
        public static string BROWSER = "Edge";
     

        private static AutoResetEvent _signal = new AutoResetEvent(false);
        private static AutoResetEvent _signalRec = new AutoResetEvent(false);
        static bool checkRec = false;



        static AppServiceConnection connection = null;
        public static bool killProcess(string processName, int ownerId)
        {

            Process[] processInstances = Process.GetProcessesByName(processName);

            foreach (Process p in processInstances)
                if (p.Id != ownerId)
                    p.Kill();
            return true;
        }
        [STAThread]
        static void Main(string[] args)
        {
    

            Process currentProcess = Process.GetCurrentProcess();
            killProcess("Edge", currentProcess.Id);
            Thread thread2 = new Thread(() => threadReadData());
            thread2.SetApartmentState(ApartmentState.STA);
            thread2.IsBackground = true;
            thread2.Start();
            Thread appServiceThread = new Thread(new ThreadStart(ThreadProc));
            appServiceThread.Start();
            Application.Run();

        }



        /// <summary>
        /// Creates the app service connection
        /// </summary>
        static async void ThreadProc()
        {
            System.IO.StreamWriter file = new System.IO.StreamWriter(Path.GetTempPath() + "\\log_HIO" + BROWSER + ".log", true);

            try
            {
                connection = new AppServiceConnection();
                connection.AppServiceName = "com.hiotech.hio";
                connection.PackageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;
                connection.RequestReceived += Connection_RequestReceived;
               
                connection.ServiceClosed += Connection_ServiceClosed;
                 AppServiceConnectionStatus status = await connection.OpenAsync();
                switch (status)
                {
                    case AppServiceConnectionStatus.Success:

                        file.WriteLine(DateTime.Now + " " + BROWSER + ": Connection established - waiting for requests");


                        break;
                    case AppServiceConnectionStatus.AppNotInstalled:

                        file.WriteLine(DateTime.Now + " " + BROWSER + ": The app AppServicesProvider is not installed.");

                        return;
                    case AppServiceConnectionStatus.AppUnavailable:

                        file.WriteLine(DateTime.Now + " " + BROWSER + ": The app AppServicesProvider is not available.");

                        return;
                    case AppServiceConnectionStatus.AppServiceUnavailable:
                        file.WriteLine(DateTime.Now + " " + BROWSER + ": " + string.Format("The app AppServicesProvider is installed but it does not provide the app service {0}.", connection.AppServiceName));

                        return;
                    case AppServiceConnectionStatus.Unknown:
                        file.WriteLine(DateTime.Now + " " + BROWSER + ": An unkown error occurred while we were trying to open an AppServiceConnection.");


                        return;
                }
                file.Close();
            }
            catch (Exception ex){

                  file.WriteLine(DateTime.Now + " " + BROWSER + ": "+ex.Message);
               
            }
            finally
            {
                 file.Close();
            }
        }
        private static void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            Application.Exit();
        }

        /// <summary>
        /// Receives message from UWP app and sends a response back
        /// </summary>
        private static void Connection_RequestReceived(AppServiceConnection sender, AppServiceRequestReceivedEventArgs args)
        {
          
            // Create a new order and set values.
            SendData sentOrder = new SendData();
            sentOrder.label = BROWSER; //set header
            string key = args.Request.Message.First().Key;
            string value = args.Request.Message.First().Value.ToString();
            var json_serializer = new JavaScriptSerializer();
            var dataValue = (IDictionary<string, object>)json_serializer.DeserializeObject(value);

            string messageType = dataValue["CMD"].ToString();

            if (messageType == "exit")
            {
                Environment.Exit(0);
            }
            if (!MessageQueue.Exists(PATHMSGSEND))
                MessageQueue.Create(PATHMSGSEND);
            MessageQueue queue = new MessageQueue(PATHMSGSEND);
            sentOrder.data = value;
            // Send the Order to the queue.
            queue.Send(sentOrder);
            queue.Purge();
            Thread threadTimeout = new Thread(() => threadTimeoutResponse(3000));
            threadTimeout.SetApartmentState(ApartmentState.STA);
            threadTimeout.Start();
            _signalRec.WaitOne();

            Thread.Sleep(200);
            if (checkRec == false)
            {
                if (messageType == "INIT")
                {
                    ValueSet focusResponse = new ValueSet();
                    focusResponse.Add("MSG","{\"CMD\":\"CONNCETION\",\"data\":\"false\"}");
                  
                    args.Request.SendResponseAsync(focusResponse).Completed += delegate { };
                    //      Write("{\"CMD\":\"CONNCETION\",\"data\":\"false\"}");

                }

                _signalRec.Reset();
            }
            else
            {
                checkRec = false;
                _signalRec.Reset();
            }

        }
        private static void threadTimeoutResponse(int miliSec)
        {
            Thread.Sleep(miliSec);
            _signalRec.Set();

        }
        private static void HandleErrorException(Exception exception)
        {
           // UninstallHook();
        }

        private static void threadReadData()
        {

            // Receive a message from a queue.
            ReceiveMessage();
        }
        public static void ReceiveMessage()
        {
            try
            {
                // Connect to the a queue on the local computer.
                if (!MessageQueue.Exists(PATHMSGREC))
                    MessageQueue.Create(PATHMSGREC);
                if (!MessageQueue.Exists(PATHMSGSEND))
                    MessageQueue.Create(PATHMSGSEND);
                MessageQueue myQueueRec = new MessageQueue(PATHMSGREC);
                MessageQueue myQueueSend = new MessageQueue(PATHMSGSEND);

                // Set the formatter to indicate body contains an Order.
                myQueueRec.Formatter = new XmlMessageFormatter(new Type[] { typeof(SendData) });
                SendData sentOrder = new SendData();
                sentOrder.label = BROWSER; //set header
                sentOrder.data = "true";
                while (true)
                {

                    try
                    {

                        // Receive and format the message. 
                        System.Messaging.Message myMessage = myQueueRec.Receive();

                        myQueueRec.Purge();

                        SendData dataPack = (SendData)myMessage.Body;
                        if (dataPack.data == "true")
                        {
                            checkRec = true;
                            _signalRec.Set();
                            continue;
                        }
                        else
                            myQueueSend.Send(sentOrder);
                        // Display message information.

                        JObject datatest = (JObject)JsonConvert.DeserializeObject<JObject>(dataPack.data.Trim().Replace("\0", ""));
                        if (datatest["CMD"].Value<string>() == "exit")
                        {
                            Environment.Exit(0);
                        }
                        //Write(dataPack.data.Trim().Replace("\0", ""));
                        ValueSet data = new ValueSet();
                        data.Add("MSG", dataPack.data.Trim().Replace("\0", ""));
                        connection.SendMessageAsync(data).Completed += delegate { };

                    }
                    // Catch other exceptions as necessary.
                    catch (Exception ex)
                    {
                        StackTrace st = new StackTrace(ex, true);
                        //Get the first stack frame
                        StackFrame frame = st.GetFrame(0);

                        //Get the file name
                        string fileName = frame.GetFileName();

                        //Get the method name
                        string methodName = frame.GetMethod().Name;

                        //Get the line number from the stack frame
                        int line = frame.GetFileLineNumber();

                        //Get the column number
                        int col = frame.GetFileColumnNumber();

                        System.IO.StreamWriter file = new System.IO.StreamWriter(Path.GetTempPath() + "\\log_HIO" + BROWSER + ".log", true);
                        file.WriteLine(DateTime.Now + " " + BROWSER + ":   " + fileName + "   " + methodName + "      " + ex.Message + line + col);
                        file.Close();
                    }


                }
            }
            catch (Exception ex)
            {

            }

        }






    }
}
