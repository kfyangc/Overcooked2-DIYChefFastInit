using System;
using System.Collections.Generic;
using BitStream;
using Team17.Online;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace OC2DIYChef.Research
{
    // Runs against the game's actual bitstream and ChefAvatarMessage under its
    // embedded Mono runtime, without initializing Unity or launching the game.
    public static class MonoCacheProbe
    {
        public static void Run()
        {
            int checks = 0;
            for (int count = 0; count <= 4; count++)
            {
                var original = new PlayerHatMessage { ServerSnapshot = count != 1,
                    Users = new ChefAvatarMessage[count], Hats = new string[count] };
                for (int i = 0; i < count; i++)
                {
                    original.Users[i] = new ChefAvatarMessage();
                    original.Users[i].Initialise(127, (User.MachineID)(i + 1), (EngagementSlot)i, (User.SplitStatus)(i % 3));
                    original.Hats[i] = i == 0 ? "@default" : i == 1 ? "巫师帽🧑‍🍳" : i == 2 ? "None" : new string('x', 255);
                }
                var buffer = new FastList<byte>(4096);
                var writer = new BitStreamWriter(buffer);
                writer.Write((byte)68, 8);
                original.Serialise(writer);
                var reader = new BitStreamReader(buffer._items, (uint)writer.GetUsedBitCount());
                if (reader.ReadByte(8) != 68) throw new Exception("Packet header changed");
                var decoded = new PlayerHatMessage();
                if (!decoded.Deserialise(reader) || !reader.EndOfStream || decoded.ServerSnapshot != original.ServerSnapshot || decoded.Users.Length != count)
                    throw new Exception("Native packet framing mismatch");
                checks++;
                for (int i = 0; i < count; i++)
                {
                    if (decoded.Hats[i] != original.Hats[i] || decoded.Users[i].m_Machine != original.Users[i].m_Machine ||
                        decoded.Users[i].m_EngagementSlot != original.Users[i].m_EngagementSlot || decoded.Users[i].m_Split != original.Users[i].m_Split)
                        throw new Exception("Native identity/hat roundtrip mismatch");
                    checks++;
                }
            }
            Console.WriteLine("PASS: " + checks + " actual game bitstream/identity codec checks on x86 Mono; no game process used.");
        }
    }
}
