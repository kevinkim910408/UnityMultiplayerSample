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


        // Driver = Socket 같은거
        m_Driver = NetworkDriver.Create();

        // 어디에 연결할지, AnyIpv4--> 머신이 돌아가는 위치의 IP
        var endpoint = NetworkEndPoint.AnyIpv4;

        // Port setting
        endpoint.Port = serverPort;

        // socket을 바인드
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);

        //바인드가 성공하면 Listen()--> 서버가 클라의 연결을 받아들일 준비상태
        else
            m_Driver.Listen();

        //커넥션 - 서버가 클라들의 접속을 알수있게(관리함) 해주는 다리, NativeList--> List와 비슷
        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }

    void Update()
    {
        // ScheduleUpdate().Complete() 이것을 불러주면서, 다음 업데이트가 레디가 됬는지 알려줌, Complete()이게 되면 이제 우리 업데이트를 해도 된다라는 느낌
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections - Connection이 제대로 안만들어져 있거나 끊어진건 리스트에서 배제
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections - 여기서 c가 connection(연결다리), 클라를 받아줌
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
            Assert.IsTrue(m_Connections[i].IsCreated);

            // cmd - 이 메세지가 어떤 메세지인지 분별
            NetworkEvent.Type cmd;

            // PopEventForConnection - 커넥션(다리)에 어떤 메세지가 와있는지, out stream에 데이터가 담김
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);

            // cmd가 empty가 아니면
            while (cmd != NetworkEvent.Type.Empty)
            {
                //데이터
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i);

                    // 허트빗 업데이트 지속적으로
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
    void SendToClient(string message, NetworkConnection c)
    {
        // writer(배달부를 생성)
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        // Json string을 bytes[]로 convert
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message), Allocator.Temp);

        // writer에 우리의 보낼 데이터를 넣어줌.
        writer.WriteBytes(bytes);

        //writer에게 전송을 명령
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

        // 우리가 보낼 c# 데이터
        PlayerUpdateMsg internalID = new PlayerUpdateMsg();
        // 이게 어떠한 메세지인지 표시
        internalID.cmd = Commands.INTERNAL_ID;
        // 뭘 보낼지 채우기 --> 연결다리에 InternalId가 int라서 string 으로
        internalID.player.id = c.InternalId.ToString();
        // 커넥션이 제대로 되는지 체크
        Assert.IsTrue(c.IsCreated);
        // 보내기
        SendToClient(JsonUtility.ToJson(internalID), c);

        /////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////// Old Clients info to new clients //////////////////////////////////////
        // 우리가 보낼 c# 데이터
        ServerUpdateMsg oldClientsInfo = new ServerUpdateMsg();
        // 이게 어떠한 메세지인지 표시
        oldClientsInfo.cmd = Commands.OLD_CLIENTS_INFO;
        // dictionary for loop 돌기
        foreach (KeyValuePair<string, NetworkObjects.NetworkPlayer> dic in allClients)
        {
            // 리스트에 값 추가
            oldClientsInfo.players.Add(dic.Value);
        }

        // 보내기
        SendToClient(JsonUtility.ToJson(oldClientsInfo), c);

        /////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////// New Clients info to old clients //////////////////////////////////////
        PlayerUpdateMsg newClientsInfo = new PlayerUpdateMsg();
        newClientsInfo.cmd = Commands.NEW_CLIENTS_INFO;
        newClientsInfo.player.id = c.InternalId.ToString();

        // New clients info 를 old clients 에게 전달
        for (int i = 0; i < m_Connections.Length; ++i)
        {
            SendToClient(JsonUtility.ToJson(newClientsInfo), m_Connections[i]);
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////





        // 리스트에 add
        m_Connections.Add(c);

        // 커넥션에 들어있는 정보를 allClients dictionary에 추가
        allClients[c.InternalId.ToString()] = new NetworkObjects.NetworkPlayer();
        allClients[c.InternalId.ToString()].id = c.InternalId.ToString();


        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    void OnData(DataStreamReader stream, int i)
    {
        // 바이트 배열을 생성
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
        // 커넥션에서 받아온 데이터들을 담기
        stream.ReadBytes(bytes);
        // 데이터를 array로 convert -> Json string으로 convert
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        // Json string -> c# class로 convert
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

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

        // 기존 정보를 가져와서 oc에 넣어줌
        foreach (KeyValuePair<string, NetworkObjects.NetworkPlayer> dic in allClients)
        {
            // 리스트에 값 추가
            oc.players.Add(dic.Value);
        }

        // 보내기
        for (int i = 0; i < m_Connections.Length; ++i)
        {
            SendToClient(JsonUtility.ToJson(oc), m_Connections[i]);
        }
    }

    // heart beat
    void HeatbeatCheck()
    {
        // List<String> 지워야할 아이디를 가진 리스트
        List<string> deleteID = new List<string>();
        foreach (KeyValuePair<string, float> dic in ClientsHeatbeatCheck)
        {
            if (Time.time - dic.Value >= 5.0f)
            {
                Debug.Log(dic.Key.ToString() + " Disconnected!");
                deleteID.Add(dic.Key);
            }
        }

        // 지울 녀석이 0이 아니면
        if (deleteID.Count != 0)
        {
            // 지운다
            for (int i = 0; i < deleteID.Count; ++i)
            {
                allClients.Remove(deleteID[i]);
                ClientsHeatbeatCheck.Remove(deleteID[i]);

            }

            // c# 클래스
            DeleteMsg dm = new DeleteMsg();
            dm.deleteID = deleteID;

            // 클라한테 뿌리기
            for (int i = 0; i < m_Connections.Length; ++i)
            {
                // disconnetcted 된 녀석은 스킵
                if (deleteID.Contains(m_Connections[i].InternalId.ToString()))
                {
                    continue;
                }

                SendToClient(JsonUtility.ToJson(dm), m_Connections[i]);
            }

        }
    }
}