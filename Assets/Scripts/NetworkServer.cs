using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;
using System.Collections.Generic;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;

    // string - internal id
    private Dictionary<string, NetworkObjects.NetworkPlayer> allClients = new Dictionary<string, NetworkObjects.NetworkPlayer>();

    // float - time, string - internal id
    private Dictionary<string, float> ClientsHeatbeatCheck = new Dictionary<string, float>();

    float lastTimeSendAllPlayerInfo;
    float intervalTimeSendingAllPlayerInfo;

    void Start()
    {
        lastTimeSendAllPlayerInfo = Time.time;
        intervalTimeSendingAllPlayerInfo = 0.03f;


        // Driver -> like socket
        m_Driver = NetworkDriver.Create();

        // 어디에 연결할지, AnyIpv4--> 머신이 돌아가는 위치의 IP
        // -> where to connect. AnyIpv4 is the location of machine's ip
        var endpoint = NetworkEndPoint.AnyIpv4;

        // Port setting
        endpoint.Port = serverPort;

        // Bind the socket
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);

        //if bind is good, server is ready to accept clients' connections
        else
            m_Driver.Listen();

        //커넥션 - 서버가 클라들의 접속을 알수있게(관리함) 해주는 다리, NativeList--> List와 비슷
        // -> m_Connections is like bridges that server knows which clients want to connect to the server. NativeList is a List
        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }

    void Update()
    {
        // ScheduleUpdate().Complete() 이것을 불러주면서, 다음 업데이트가 레디가 됬는지 알려줌, Complete()이게 되면 이제 우리 업데이트를 해도 된다라는 느낌
        // -> If Complete(), we can update ours.
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections - Connection이 제대로 안만들어져 있거나 끊어진건 리스트에서 배제
        // -> CleanUpConnections helps List to remove unconnected connections, or not completed connections(bridges)
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections - 여기서 c가 connection(연결다리), 클라를 받아줌
        // - > c is accepting clients
        NetworkConnection c = m_Driver.Accept();
        while (c != default(NetworkConnection))
        {
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }


        // Read Incoming Messages 
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            // 커넥션들이 다 잘 되는지 체크
            // -> check all the connections whether working or not
            Assert.IsTrue(m_Connections[i].IsCreated);

            // cmd - 이 메세지가 어떤 메세지인지 분별
            // cmd(enum) helps server to know the next actions
            NetworkEvent.Type cmd;

            // PopEventForConnection - 커넥션(다리)에 어떤 메세지가 와있는지, out stream에 데이터가 담김
            // PopEventForConnection - which messages are on the connections(bridges)
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);

            // if cme is not empty == there are values
            while (cmd != NetworkEvent.Type.Empty)
            {
                //data
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i);

                    // update heartbeat time to time
                    ClientsHeatbeatCheck[m_Connections[i].InternalId.ToString()] = Time.time;
                }
                // Disconnect
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }

        //Every interval, send all info to all clients
        if (Time.time - lastTimeSendAllPlayerInfo >= intervalTimeSendingAllPlayerInfo)
        {
            lastTimeSendAllPlayerInfo = Time.time;

            SendInfoToAllClients();
            //Debug.Log("Send all player info to client");
        }

        HeatbeatCheck();
    }

    // C# 클래스를 JSON String 으로 convert해서 message
    // -> c# class convert to Json String, and convert again to byte[]
    void SendToClient(string message, NetworkConnection c)
    {
        // writer(배달부를 생성)
        // -> writer is a delivery man
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);

        // Convert Json string to byte[]
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message), Allocator.Temp);

        // writer에 우리의 보낼 데이터를 넣어줌.
        // -> give data to writer
        writer.WriteBytes(bytes);

        //writer에게 전송을 명령
        // -> let writer to send data
        m_Driver.EndSend(writer);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c)
    {
        Debug.Log("Accepted a connection");

        /////////////////////////////// Send internal id to new clients //////////////////////////////////////

        // 우리가 보낼 c# 데이터 --> the data what we want to send (c# class)
        PlayerUpdateMsg internalID = new PlayerUpdateMsg();

        // 이게 어떠한 메세지인지 표시 -->  mark the data let clients know about this.
        internalID.cmd = Commands.INTERNAL_ID;

        // 뭘 보낼지 채우기 --> 연결다리에 InternalId가 int라서 string 으로
        // -> fill the data (in the connection, InternalId is int type. so need to convert to string)
        internalID.player.id = c.InternalId.ToString();

        // 커넥션이 제대로 되는지 체크
        // check if connection is good
        Assert.IsTrue(c.IsCreated);

        // Send data to new clients
        SendToClient(JsonUtility.ToJson(internalID), c);

        /////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////// Old Clients info to new clients //////////////////////////////////////

        // 우리가 보낼 c# 데이터 --> the data what we want to send (c# class)
        ServerUpdateMsg oldClientsInfo = new ServerUpdateMsg();

        // 이게 어떠한 메세지인지 표시 -->  mark the data let clients know about this.
        oldClientsInfo.cmd = Commands.OLD_CLIENTS_INFO;

        // dictionary for loop
        foreach (KeyValuePair<string, NetworkObjects.NetworkPlayer> dic in allClients)
        {
            // add values
            oldClientsInfo.players.Add(dic.Value);
        }

        // send to new clients
        SendToClient(JsonUtility.ToJson(oldClientsInfo), c);

        /////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////// New Clients info to old clients //////////////////////////////////////
        PlayerUpdateMsg newClientsInfo = new PlayerUpdateMsg();
        newClientsInfo.cmd = Commands.NEW_CLIENTS_INFO;
        newClientsInfo.player.id = c.InternalId.ToString();

        // send New clients info to old clients 
        for (int i = 0; i < m_Connections.Length; ++i)
        {
            SendToClient(JsonUtility.ToJson(newClientsInfo), m_Connections[i]);
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////





        // add to connection list
        m_Connections.Add(c);

        // 커넥션에 들어있는 정보를 allClients dictionary에 추가
        // -> add info in the connection to allClients dictionary
        allClients[c.InternalId.ToString()] = new NetworkObjects.NetworkPlayer();
        allClients[c.InternalId.ToString()].id = c.InternalId.ToString();


        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    void OnData(DataStreamReader stream, int i)
    {
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Data from client to server: (ReadBytes)->bytes-> (GetString)->JSON string -> (FromJson)->c# class

        // 바이트 배열을 생성
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
        // 커넥션에서 받아온 데이터들을 담기
        stream.ReadBytes(bytes);
        // 데이터를 array로 convert -> Json string으로 convert
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        // Json string -> c# class로 convert
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        // messages
        switch (header.cmd)
        {
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!");
                break;

            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                //Debug.Log("Player update message received!");
                //Debug.Log(puMsg.player.cubPos);

                // update clients
                UpdateClient(puMsg);

                break;

            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
                break;

            default:
                Debug.Log("SERVER ERROR: Unrecognized message received!");
                break;
        }
    }

    void OnDisconnect(int i)
    {
        Debug.Log("Client disconnected from server");
        m_Connections[i] = default(NetworkConnection);
    }

    void UpdateClient(PlayerUpdateMsg data)
    {
        if (allClients.ContainsKey(data.player.id))
            allClients[data.player.id].cubPos = data.player.cubPos;
    }

    void SendInfoToAllClients()
    {
        ServerUpdateMsg oc = new ServerUpdateMsg();

        // get info from allClients and add to oc
        foreach (KeyValuePair<string, NetworkObjects.NetworkPlayer> dic in allClients)
        {
            oc.players.Add(dic.Value);
        }

        // send
        for (int i = 0; i < m_Connections.Length; ++i)
        {
            SendToClient(JsonUtility.ToJson(oc), m_Connections[i]);
        }
    }

    // heart beat
    void HeatbeatCheck()
    {
        // List<String> - thie list of that contains IDs which need to be deleted.
        List<string> deleteID = new List<string>();
        foreach (KeyValuePair<string, float> dic in ClientsHeatbeatCheck)
        {
            if (Time.time - dic.Value >= 5.0f)
            {
                Debug.Log(dic.Key.ToString() + " Disconnected!");
                deleteID.Add(dic.Key);
            }
        }

        // if there is data that need to be removed
        if (deleteID.Count != 0)
        {
            // delete
            for (int i = 0; i < deleteID.Count; ++i)
            {
                allClients.Remove(deleteID[i]);
                ClientsHeatbeatCheck.Remove(deleteID[i]);

            }

            // make c# class to give to clients
            DeleteMsg dm = new DeleteMsg();
            dm.deleteID = deleteID;

            // send to clients
            for (int i = 0; i < m_Connections.Length; ++i)
            {
                // but we dont need to send already disconnected values.
                if (deleteID.Contains(m_Connections[i].InternalId.ToString()))
                {
                    // skip
                    continue;
                }

                SendToClient(JsonUtility.ToJson(dm), m_Connections[i]);
            }

        }
    }
}