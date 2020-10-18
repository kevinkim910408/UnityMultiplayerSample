using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.Collections.Generic;

public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    
    public string serverIP;
    public ushort serverPort;


    [SerializeField]
    GameObject clients;

    [SerializeField]
    Transform transformPosition;

    string id;

    Dictionary<string, GameObject> listOfOldClients = new Dictionary<string, GameObject>();
    
    void Start ()
    {
        //serverIP = "3.20.240.191"; // server
        serverIP = "127.0.0.1"; //local
        m_Driver = NetworkDriver.Create();

        // 서버에 연결되는 다리(이걸 이용해서 서버에 메세지를 보냄)
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort);

        // 서버의 커넥션하고는 다른 커넥션. ( 총 2개의 다리가 있다고 생각 - 서버, 클라 각각 한개씩)
        m_Connection = m_Driver.Connect(endpoint);
    }
    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        // PopEvent - 서버쪽에서 무슨 데이터를 줬는지 검사.
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            //서버가 진짜 데이터를보낸거
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }

    void SendToServer(string message)
    {
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect()
    {
        Debug.Log("We are now connected to the server");

        InvokeRepeating("UpdateClientsPosition", 0.1f, 0.03f);

        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = m_Connection.InternalId.ToString();
        // SendToServer(JsonUtility.ToJson(m));
    }

    void OnData(DataStreamReader stream)
    {
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
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

               for(int i = 0; i < suMsg.players.Count; ++i)
               {
                    Debug.Log(suMsg.players[i].id + "  " + suMsg.players[i].cubPos);
               }
                break;

            case Commands.INTERNAL_ID:
                // get internal id from server
                PlayerUpdateMsg internalID = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                //Debug.Log(internalID.player.id);
                id = internalID.player.id;
                break;

            case Commands.OLD_CLIENTS_INFO:
                // get old clients info  from server
                ServerUpdateMsg oldClientsInfo = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                //for(int i = 0; i < oldClientsInfo.players.Count; ++i)
                //{
                //    Debug.Log(oldClientsInfo.players[i].id);
                //}
                // spawn old clients
                SpawnOldClients(oldClientsInfo);
                break;

            case Commands.NEW_CLIENTS_INFO:
                PlayerUpdateMsg newClientsInfo = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                // spawn new client
                SpawnNewClients(newClientsInfo);
                break;

            default:
                Debug.Log("Unrecognized message received!");
                break;
        }
    }

    void Disconnect()
    {
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect()
    {
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }   
    
    public void SpawnOldClients(ServerUpdateMsg oldClientsDatas)
    {
        for( int i = 0; i < oldClientsDatas.players.Count; ++i)
        {
            GameObject oc = Instantiate(clients);
            listOfOldClients[oldClientsDatas.players[i].id] = oc;
            oc.transform.position = oldClientsDatas.players[i].cubPos;
        }
    }

    public void SpawnNewClients(PlayerUpdateMsg newClientsDatas)
    {
        GameObject nc = Instantiate(clients);
        listOfOldClients[newClientsDatas.player.id] = nc;
    }

    public void UpdateClientsPosition()
    {
        // c# 클래스
        PlayerUpdateMsg data = new PlayerUpdateMsg();
        data.player.id = id;
        data.player.cubPos = transformPosition.position;

        // json string
        SendToServer(JsonUtility.ToJson(data));
    }
}