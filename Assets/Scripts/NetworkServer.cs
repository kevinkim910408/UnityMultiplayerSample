using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;

    void Start ()
    {
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
                }
                // Disconnect
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }

    // C# 클래스를 JSON String 으로 convert해서 message
    void SendToClient(string message, NetworkConnection c)
    {
        // writer(배달부를 생성)
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        // Json string을 bytes[]로 convert
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);

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
        // 리스트에 add
        m_Connections.Add(c);
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



        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    void OnData(DataStreamReader stream, int i)
    {
        // 바이트 배열을 생성
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        // 커넥션에서 받아온 데이터들을 담기
        stream.ReadBytes(bytes);
        // 데이터를 array로 convert -> Json string으로 convert
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        // Json string -> c# class로 convert
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd)
        {
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!");
                break;

            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
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

    
}