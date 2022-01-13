using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommunicationServices
{
    public class CommunicationService
    {
        private class CommunicationObject
        {
            public Socket socket;             // Socket
            public Socket workSocket;         // Client Socket
            public string ipAdress;           // IP 주소
            public int port;                  // 포트 주소
            public byte[] buffer
                = new byte[ushort.MaxValue];  // 받을 Buffer 크기 (65536)
        }

        private CommunicationObject obj = null;
        public DataUtil util = new DataUtil();
        private bool queueControl = false;

        public class Server : CommunicationService
        {
            public void Initialize(int port)
            {
                Task.Run(() =>
                {
                    var iPEndPoint = new IPEndPoint(IPAddress.Any, port);
                    var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    Task.Run(() => DeQueue());
                    try
                    {
                        server.Bind(iPEndPoint);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"{e}");
                        throw;
                    }
                    server.Listen(20);

                    Console.WriteLine("소켓을 열어 연결을 허용합니다.");
                    var client = server.Accept();
                    Console.WriteLine($"{client.LocalEndPoint} 소켓이 연결되었습니다.");
                    obj = new CommunicationObject() { socket = server, workSocket = client };
                    Receive(obj);
                    RasiseConnected();

                    try
                    {
                        while (true)
                        {
                            if (!IsConnect(client))
                            {

                                RasiseDisconnected();
                                Console.WriteLine("소켓을 열어 연결을 허용합니다.");
                                client = server.Accept();
                                Console.WriteLine($"{client.LocalEndPoint} 소켓이 연결되었습니다.");
                                obj = new CommunicationObject() { socket = server, workSocket = client };
                                Receive(obj);
                                RasiseConnected();
                            }

                            Thread.Sleep(1000);
                        }
                    }
                    catch (System.Exception ex) { ex.ToString(); }
                });
            }

            public void Finalization()
            {
                obj?.socket?.Close();
                obj?.workSocket?.Close();
                Console.WriteLine("서버를 닫습니다.");
            }
        }

        public class Client : CommunicationService
        {
            private ManualResetEvent connectDone = new ManualResetEvent(false);
            public void Initialize(string ipAdress, int port)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var remoteEP = new IPEndPoint(IPAddress.Parse(ipAdress), port);
                        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        obj = new CommunicationObject() { socket = client, workSocket = client, ipAdress = ipAdress, port = port };
                        Task.Run(() => DeQueue());

                        client.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), obj);
                        connectDone.WaitOne();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                });
            }

            private void ConnectCallback(IAsyncResult ar)
            {
                var obj = ar.AsyncState as CommunicationObject;

                try
                {
                    obj.workSocket.EndConnect(ar);
                    Console.WriteLine($"{obj.workSocket.RemoteEndPoint} 소켓이 연결되었습니다.");
                    connectDone.Set();
                    Receive(obj);
                    RasiseConnected();

                    Task.Run(() =>
                    {
                        bool loopControl = true;
                        while (loopControl)
                        {
                            var a = IsConnect(obj.workSocket);
                            if (!IsConnect(obj.workSocket))
                            {
                                RasiseDisconnected();
                                loopControl = false;
                                Console.WriteLine($"{obj.ipAdress }소켓 연결이 끊어져 재시도합니다");
                                obj.workSocket.Disconnect(true);
                                obj.workSocket.BeginConnect(obj.ipAdress, obj.port, new AsyncCallback(ConnectCallback), obj);
                            }
                            Thread.Sleep(1000);
                        }
                    });
                }
                catch (Exception e)
                {
                    // Ip주소가 아예 다를 때, 발생하는 이벤트를 잡기 위한 조건문
                    if (e is SocketException && (e as SocketException).ErrorCode == 10051)
                        Thread.Sleep(1000);

                    Console.WriteLine($"{obj.ipAdress } 소켓 연결을 재시도합니다.");
                    try
                    {
                        obj.workSocket.BeginConnect(obj.ipAdress, obj.port, new AsyncCallback(ConnectCallback), obj);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex}");
                    }
                }
            }

            public void Finalization()
            {
                obj?.workSocket?.Close();
                Console.WriteLine("클라이언트를 닫습니다.");
            }
        }

        private object lockObject = new object();

        public delegate void ConnectedHandler();
        public event ConnectedHandler Connected;
        protected virtual void RasiseConnected()
            => Connected?.Invoke();

        public delegate void ReceivedHandler(byte[] packet);
        public event ReceivedHandler Received;
        protected virtual void RasiseReceived(byte[] packet)
            => Received?.Invoke(packet);

        public delegate void DisconnectedHandler();
        public event DisconnectedHandler Disconnected;
        protected virtual void RasiseDisconnected()
            => Disconnected?.Invoke();

        private Queue<byte[]> queue = new Queue<byte[]>();

        public bool IsConnected { get; set; }       // 통신이 연결되었는지 판단하는 Bool

        private bool IsConnect(Socket client)
            => IsConnected = !(client.Poll(0, SelectMode.SelectRead) & client.Available == 0);

        private void EnQueueData(byte[] bytes)
            => queue.Enqueue(bytes);

        private void DeQueue()
        {
            while (true)
            {
                if (queue.Count > 0)
                {
                    lock (lockObject)
                        RasiseReceived(queue.Dequeue());
                }
                else
                {
                    Thread.Sleep(10);
                    Task.Yield();
                }
            }
        }

        private void Receive(CommunicationObject obj)
        {
            try
            {
                obj.workSocket.BeginReceive(obj.buffer, 0, obj.buffer.Length, 0, new AsyncCallback(ReceiveCallback), obj);
            }
            catch (Exception e) { e.ToString(); }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                var obj = ar.AsyncState as CommunicationObject;

                // 받은 데이터를 읽는 부분
                int bytesRead = obj.workSocket.EndReceive(ar);

                if (bytesRead > 0)
                {
                    var data = new byte[bytesRead];
                    Array.Copy(obj.buffer, 0, data, 0, data.Length);

                    lock (lockObject)
                    {
                        EnQueueData(data);
                        obj.workSocket.BeginReceive(obj.buffer, 0, obj.buffer.Length, 0, new AsyncCallback(ReceiveCallback), obj);
                    }
                }
            }
            catch (Exception e) { e.ToString(); }
        }

        public void Send(byte[] packet)
        {
            try
            {
                obj.workSocket.BeginSend(packet, 0, packet.Length, 0, new AsyncCallback(SendCallback), obj);
            }
            catch (Exception e) { e.ToString(); }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                obj = ar.AsyncState as CommunicationObject;
                int bytesSent = obj.workSocket.EndSend(ar);
            }
            catch (Exception e) { e.ToString(); }
        }

        public class DataUtil
        {
            public char[] StringToCharArray(string str, int length)
            {
                var tempArray = new char[length];

                for (int i = 0; i < str.ToCharArray().Length; i++)
                {
                    tempArray[i] = str.ToCharArray()[i];
                }

                return tempArray;
            }

            // 구조체를 byte 배열로
            public byte[] StructureToByte(object obj)
            {
                int datasize = Marshal.SizeOf(obj);
                IntPtr buff = Marshal.AllocHGlobal(datasize);
                Marshal.StructureToPtr(obj, buff, false);
                byte[] data = new byte[datasize];
                Marshal.Copy(buff, data, 0, datasize);
                Marshal.FreeHGlobal(buff);
                return data;
            }

            //구조체를 byte 배열로
            public byte[] StructureToByte(params object[] objs)
            {
                int totalsize = 0;
                List<byte[]> list = new List<byte[]>();
                foreach (object obj in objs)
                {
                    int datasize = Marshal.SizeOf(obj);
                    IntPtr buff = Marshal.AllocHGlobal(datasize);
                    Marshal.StructureToPtr(obj, buff, false);
                    byte[] data = new byte[datasize];
                    Marshal.Copy(buff, data, 0, datasize);
                    Marshal.FreeHGlobal(buff);
                    list.Add(data);
                    totalsize += datasize;
                }
                byte[] datas = new byte[totalsize];
                int length = 0;
                foreach (byte[] data in list)
                {
                    Array.Copy(data, 0, datas, length, data.Length);
                    length += data.Length;
                }

                return datas;
            }

            // byte 구조체 합치기
            public byte[] CombineToByte(byte[] array, object obj)
                => CombineToByte(array, StructureToByte(obj));
            public byte[] CombineToByte(object obj, byte[] array)
                => CombineToByte(StructureToByte(obj), array);
            public byte[] CombineToByte(object obj1, object obj2)
                => CombineToByte(StructureToByte(obj1), StructureToByte(obj2));
            public byte[] CombineToByte(byte[] array1, byte[] array2)
            {
                byte[] rv = new byte[array1.Length + array2.Length];
                Buffer.BlockCopy(array1, 0, rv, 0, array1.Length);
                Buffer.BlockCopy(array2, 0, rv, array1.Length, array2.Length);
                return rv; // 배열을 리턴
            }

            private object ByteToStructure(byte[] data, Type type)
            {
                try
                {
                    IntPtr buff = Marshal.AllocHGlobal(data.Length);
                    Marshal.Copy(data, 0, buff, data.Length);
                    object obj = Marshal.PtrToStructure(buff, type);
                    Marshal.FreeHGlobal(buff);

                    return obj;
                }
                catch
                {
                    return null;
                }
            }

            public T ByteToStructure<T>(byte[] data)
               => (T)ByteToStructure(data, typeof(T));

            //byte 배열을 구조체로
            private object ByteToStructure(byte[] data, Type type, int start)
            {
                try
                {
                    var size = Marshal.SizeOf(type);
                    var buff = Marshal.AllocHGlobal(size);
                    Marshal.Copy(data, start, buff, size);

                    var obj = Marshal.PtrToStructure(buff, type);
                    Marshal.FreeHGlobal(buff);

                    return obj;
                }
                catch (System.Exception ex)
                {
                    return null;
                }
            }

            public T ByteToStructure<T>(byte[] data, int start)
               => (T)ByteToStructure(data, typeof(T), start);

            public string WriteFields(object obj)
            {
                string result = null;

                var fieldInfo = obj.GetType().GetFields();
                for (int i = 0; i < fieldInfo.Count(); i++)
                    result += ($"{fieldInfo[i].Name} =  {fieldInfo[i].GetValue(obj)} {Environment.NewLine}");

                return result;
            }

            public string WriteProperties(object obj)
            {
                string result = null;

                var propertyInfo = obj.GetType().GetProperties();
                for (int i = 0; i < propertyInfo.Count(); i++)
                    result += ($"{propertyInfo[i].Name} =  {propertyInfo[i].GetValue(obj)} {Environment.NewLine}");

                return result;
            }
        }
    }
}
