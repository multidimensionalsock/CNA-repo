﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading.Channels;
using System.Collections.Concurrent;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace ServerProj
{
    internal class Server
    {

        static void Main()
        {
            Server server = new Server("127.0.0.1", 4444);
            server.Start();
            server.Stop();
        }

        TcpListener m_TcpListener;
        ConcurrentDictionary<int, ConnectedClient> m_clients;

        public Server(String ipAddress, int port)
        {
            IPAddress ip = IPAddress.Parse(ipAddress);
            m_TcpListener = new TcpListener(ip, port);
            
        }

        public void Start()
        {
            m_TcpListener.Start();
            m_clients = new ConcurrentDictionary<int, ConnectedClient>();
            int clientIndex = 0;

            while (true)
            {
                Console.WriteLine("listening...");
                Socket socket = m_TcpListener.AcceptSocket();
                ConnectedClient client = new ConnectedClient(socket); ;
                Console.WriteLine("connection made");
                int index = clientIndex;
                clientIndex++;
                m_clients.TryAdd(index, client);
                Thread thread = new Thread(() => { ClientMethod(index); });
                thread.Start();
            }
            
        }

        private void ClientMethod(int index)
        {
            Packets.Packet recievedMessage;
            ConnectedClient client = m_clients[index];

            while ((recievedMessage = client.Read()) != null)
            {
                switch (recievedMessage.m_packetType)
                {
                    case Packets.Packet.PacketType.ChatMessage:
                        Packets.ChatMessagePacket chatPacket = (Packets.ChatMessagePacket)recievedMessage;
                        m_clients[index].Send(new Packets.ChatMessagePacket(m_clients[index].GetReturnMessage(chatPacket.m_message)));
                        break;
                    case Packets.Packet.PacketType.RSAMessage:
                        Packets.RSAPacket rsapacket = (Packets.RSAPacket)recievedMessage;
                        m_clients[index].m_ClientKey = FromXml(rsapacket.m_key);
                        break;
                }
            }
            m_clients[index].Close();
            ConnectedClient c;
            m_clients.TryRemove(index, out c);
        }

        public RSAParameters FromXml(string key)
        {
            StringReader reader = new StringReader(key);
            XmlSerializer serializer = new XmlSerializer(typeof(RSAParameters));
            return (RSAParameters)serializer.Deserialize(reader);
        }

        public void Stop()
        {
            m_TcpListener.Stop();
        }

        
    }

    internal class ConnectedClient 
    {
        private Socket m_socket;
        private NetworkStream m_stream;
        private BinaryReader m_reader;
        public BinaryWriter m_writer;
        BinaryFormatter m_formatter;
        private object m_readLock;
        private object m_writeLock;
        RSACryptoServiceProvider m_RSAProvider;
        RSAParameters m_PublicKey;
        public RSAParameters m_PrivateKey;
        public RSAParameters m_ClientKey;

        public ConnectedClient(Socket socket)
        {
            m_writeLock = new object();
            m_readLock = new object();
            m_socket = socket;
            m_RSAProvider = new RSACryptoServiceProvider(1024);
            m_PublicKey = m_RSAProvider.ExportParameters(false);
            m_PrivateKey = m_RSAProvider.ExportParameters(true);
            m_stream = new NetworkStream(socket, true);
            m_reader = new BinaryReader(m_stream, Encoding.UTF8);
            m_writer = new BinaryWriter(m_stream, Encoding.UTF8);
            m_formatter = new BinaryFormatter();
            sendRSAkey();
        }

        public void Close()
        {
            m_stream.Close();
            m_reader.Close();
            m_writer.Close();
            m_socket.Close();
        }

        public Packets.Packet Read()
        {
            lock (m_readLock)
            {
                int numberOfBytes;
                if ((numberOfBytes = m_reader.ReadInt32()) != -1)
                {
                    byte[] buffer = m_reader.ReadBytes(numberOfBytes);
                    MemoryStream m_memoryStream = new MemoryStream(buffer);
                    return m_formatter.Deserialize(m_memoryStream) as Packets.Packet;
                }
                return null;
            }
        }

        public void Send(Packets.Packet message)
        {
            lock (m_writeLock)
            {
                MemoryStream m_memoryStream = new MemoryStream();
                m_formatter.Serialize(m_memoryStream, message);
                byte[] buffer = m_memoryStream.GetBuffer();
                m_writer.Write(buffer.Length);
                m_writer.Write(buffer);
                m_writer.Flush();
            }

        }

        private void sendRSAkey()
        {
            Packets.RSAPacket newPacket = new Packets.RSAPacket(m_PrivateKey);
            MemoryStream m_memoryStream = new MemoryStream();
            m_formatter.Serialize(m_memoryStream, newPacket);
            byte[] buffer = m_memoryStream.GetBuffer();
            m_writer.Write(buffer.Length);
            m_writer.Write(buffer);
            m_writer.Flush();
        }

        public byte[] GetReturnMessage(byte[] code)
        {
            return EncryptString("hello");
        }

        private byte[] Encrypt(byte[] data)
        {
            lock (m_RSAProvider) ;
            m_RSAProvider.ImportParameters(m_PublicKey);
            return m_RSAProvider.Encrypt(data, true);
        }

        private byte[] Decrypt(byte[] data)
        {
            lock (m_RSAProvider) ;
            m_RSAProvider.ImportParameters(m_ClientKey);
            return m_RSAProvider.Decrypt(data, true);
        }

        private byte[] EncryptString(string message)
        {
            byte[] byteArray;
            byteArray = Encoding.UTF8.GetBytes(message);
            return Encrypt(byteArray);
        }

        private string DecryptString(byte[] message)
        {
            message = Decrypt(message);
            return Encoding.UTF8.GetString(message);
        }

        
    }

}