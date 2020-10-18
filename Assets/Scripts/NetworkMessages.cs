using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NetworkMessages
{
    // messages let server and clients know what motion need to act
    public enum Commands{
        PLAYER_UPDATE,
        SERVER_UPDATE,
        HANDSHAKE,
        PLAYER_INPUT,
        INTERNAL_ID,
        OLD_CLIENTS_INFO,
        NEW_CLIENTS_INFO,
        DISCONNECTED,
    }

    [System.Serializable]
    public class NetworkHeader{
        public Commands cmd;
    }

    [System.Serializable]
    public class HandshakeMsg:NetworkHeader{
        public NetworkObjects.NetworkPlayer player;
        public HandshakeMsg(){      // Constructor
            cmd = Commands.HANDSHAKE;
            player = new NetworkObjects.NetworkPlayer();
        }
    }

    // PlayerUpdateMsg - This is gonna be me
    [System.Serializable]
    public class PlayerUpdateMsg:NetworkHeader{
        // plater - id, color, pos
        public NetworkObjects.NetworkPlayer player;
        public PlayerUpdateMsg(){      // Constructor
            cmd = Commands.PLAYER_UPDATE;
            player = new NetworkObjects.NetworkPlayer();
        }
    };

    public class PlayerInputMsg:NetworkHeader{
        public Input myInput;
        public PlayerInputMsg(){
            cmd = Commands.PLAYER_INPUT;
            myInput = new Input();
        }
    }

    // PlayerUpdateMsg - This is gonna be other clients
    [System.Serializable]
    public class  ServerUpdateMsg:NetworkHeader{
        public List<NetworkObjects.NetworkPlayer> players;
        public ServerUpdateMsg(){      // Constructor
            cmd = Commands.SERVER_UPDATE;
            players = new List<NetworkObjects.NetworkPlayer>();
        }
    }

    // DeleteMsg - delete message that let server and clinets know internal IDs
    [System.Serializable]
    public class DeleteMsg : NetworkHeader
    {
        public List<string> deleteID;
        public DeleteMsg()
        {      // Constructor
            cmd = Commands.DISCONNECTED;
            deleteID = new List<string>();
        }
    }

}

namespace NetworkObjects
{
    [System.Serializable]
    public class NetworkObject{
        public string id;
    }
    [System.Serializable]
    public class NetworkPlayer : NetworkObject{
        public Color cubeColor;
        public Vector3 cubPos;

        public NetworkPlayer(){
            cubeColor = new Color();
        }
    }
}
