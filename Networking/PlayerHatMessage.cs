using BitStream;
using System;
using System.Text;
using Team17.Online;
using Team17.Online.Multiplayer.Messaging;

namespace OC2DIYChef
{
    // Separate from the original DIY chef packet, whose layout remains unchanged.
    public sealed class PlayerHatMessage : Serialisable
    {
        public bool ServerSnapshot;
        public ChefAvatarMessage[] Users = new ChefAvatarMessage[0];
        public string[] Hats = new string[0];

        public void Serialise(BitStreamWriter writer)
        {
            writer.Write(ServerSnapshot);
            writer.Write((byte)Users.Length, 3);
            for (int i = 0; i < Users.Length; i++)
            {
                Users[i].Serialise(writer);
                var bytes = Encoding.UTF8.GetBytes(Hats[i]);
                if (bytes.Length > 255) throw new InvalidOperationException("Hat name exceeds 255 UTF-8 bytes");
                writer.Write((byte)bytes.Length, 8);
                foreach (byte value in bytes) writer.Write(value, 8);
            }
        }

        public bool Deserialise(BitStreamReader reader)
        {
            ServerSnapshot = reader.ReadBit();
            int count = reader.ReadByte(3);
            if (count > 4) return false;
            Users = new ChefAvatarMessage[count]; Hats = new string[count];
            for (int i = 0; i < count; i++)
            {
                Users[i] = new ChefAvatarMessage();
                Users[i].Deserialise(reader);
                var bytes = new byte[reader.ReadByte(8)];
                for (int n = 0; n < bytes.Length; n++) bytes[n] = reader.ReadByte(8);
                Hats[i] = Encoding.UTF8.GetString(bytes);
            }
            return true;
        }
    }

}
