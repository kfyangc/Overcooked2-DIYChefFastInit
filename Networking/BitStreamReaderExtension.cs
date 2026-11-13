using BitStream;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Team17.Online;

namespace OC2DIYChef.Extension
{
    public static class BitStreamReaderExtension
    {
        private static readonly FieldInfo fieldInfo_bufferLengthInBits = AccessTools.Field(typeof(BitStreamReader), "_bufferLengthInBits");
        private static readonly FieldInfo fieldInfo_cbitsInPartialByte = AccessTools.Field(typeof(BitStreamReader), "_cbitsInPartialByte");
        private static readonly FieldInfo fieldInfo_partialByte = AccessTools.Field(typeof(BitStreamReader), "_partialByte");
        private static readonly FieldInfo fieldInfo_byteArray = AccessTools.Field(typeof(BitStreamReader), "_byteArray");
        private static readonly FieldInfo fieldInfo_byteArrayIndex = AccessTools.Field(typeof(BitStreamReader), "_byteArrayIndex");

        public static byte ReadByteAhead(this BitStreamReader instance, int countOfBits)
        {
            if (instance.EndOfStream) return 0;
            if (countOfBits > 8 || countOfBits <= 0) return 0;
            if ((long)countOfBits > (long)(ulong)(uint)fieldInfo_bufferLengthInBits.GetValue(instance)) return 0;
            byte b;

            int cbitsInPartialByte = (int)fieldInfo_cbitsInPartialByte.GetValue(instance);
            byte partialByte = (byte)fieldInfo_partialByte.GetValue(instance);
            if (cbitsInPartialByte >= countOfBits)
            {
                int num = 8 - countOfBits;
                b = (byte)(partialByte >> num);
            }
            else
            {
                byte[] byteArray = (byte[])fieldInfo_byteArray.GetValue(instance);
                byte b2 = byteArray[(int)fieldInfo_byteArrayIndex.GetValue(instance)];
                int num2 = 8 - countOfBits;
                b = (byte)(partialByte >> num2);
                int num3 = num2 + cbitsInPartialByte;
                b |= (byte)(b2 >> num3);
            }
            return b;
        }
    }
}
