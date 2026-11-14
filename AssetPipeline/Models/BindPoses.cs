using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Team17.Online;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using BitStream;
using OC2DIYChef.Extension;
using System.Collections;

namespace OC2DIYChef
{
    public static class BindPoses
    {
        static Dictionary<string, Matrix4x4> bindposesMatrices;

        public static Matrix4x4 GetMatrix(string name)
        {
            if (bindposesMatrices == null)
            {
                bindposesMatrices = new Dictionary<string, Matrix4x4>();
                bindposesMatrices.Add("HeldItem", new Matrix4x4(
                    new Vector4(0.90715f, 0.41948f, 0.03323f, -0.66467f),
                    new Vector4(-0.17699f, 0.30871f, 0.93455f, -0.18004f),
                    new Vector4(0.38177f, -0.85366f, 0.35429f, 0.15180f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                //bindposesMatrices.Add("HeldItem", new Matrix4x4(
                //    new Vector4(0.90715f, 0.36555f, 0.20844f, 0.03940f),
                //    new Vector4(-0.17699f, -0.11794f, 0.97712f, 0.66710f),
                //    new Vector4(0.38177f, -0.92329f, -0.04229f, 0.24967f),
                //    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("HatBase", new Matrix4x4(
                    new Vector4(0.00000f, -0.80064f, 0.59915f, 0.00000f),
                    new Vector4(0.00000f, 0.59915f, 0.80064f, 0.00000f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Head", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.58614f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Eyes", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.91415f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.26908f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Eyes2_Blinks", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.91415f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.26908f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("LeftHand", new Matrix4x4(
                    new Vector4(0.84462f, -0.50944f, -0.16458f, 0.66203f),
                    new Vector4(0.28076f, 0.15975f, 0.94639f, -0.02605f),
                    new Vector4(-0.45583f, -0.84555f, 0.27796f, 0.11712f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("RightHand", new Matrix4x4(
                    new Vector4(0.58976f, -0.07015f, -0.80453f, -0.21272f),
                    new Vector4(-0.71165f, -0.51609f, -0.47667f, 0.66973f),
                    new Vector4(-0.38177f, 0.85366f, -0.35429f, -0.15180f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Jnt_Body", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.00000f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Jnt_Tail", new Matrix4x4(
                    new Vector4(-0.08938f, -0.96321f, 0.25346f, 0.43950f),
                    new Vector4(-0.94307f, 0.00000f, -0.33258f, -0.13868f),
                    new Vector4(0.32035f, -0.26875f, -0.90838f, -0.28563f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("NeckTie", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.57805f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.27746f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Body_Top", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.58614f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("HatTop", new Matrix4x4(
                    new Vector4(0.99971f, -0.02359f, 0.00477f, 0.02496f),
                    new Vector4(0.02364f, 0.99966f, -0.01064f, -1.38675f),
                    new Vector4(-0.00452f, 0.01075f, 0.99993f, 0.35745f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Hair", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 1.16386f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, -0.10810f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Eyebrows", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.98427f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.26070f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Jnt_Wheelchair", new Matrix4x4(
                    new Vector4(1.00000f, 0.00000f, 0.00000f, -0.00383f),
                    new Vector4(0.00000f, 1.00000f, 0.00000f, -0.43046f),
                    new Vector4(0.00000f, 0.00000f, 1.00000f, 0.22224f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Tentacles", new Matrix4x4(
                    new Vector4(0.00000f, -1.00000f, 0.00000f, 0.61447f),
                    new Vector4(-1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, -1.00000f, 0.02835f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Base", new Matrix4x4(
                    new Vector4(1.00000f, 0.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 1.00000f, 0.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, 1.00000f, 0.00000f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("Attach_Backpack", new Matrix4x4(
                    new Vector4(1.00000f, 0.00000f, 0.00000f, -0.00459f),
                    new Vector4(0.00000f, 1.00000f, 0.00000f, -0.43413f),
                    new Vector4(0.00000f, 0.00000f, 1.00000f, 0.22962f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("LeftWrist", new Matrix4x4(
                    new Vector4(1.00000f, 0.00000f, 0.00000f, 0.49847f),
                    new Vector4(0.00000f, 1.00000f, 0.00000f, -0.44045f),
                    new Vector4(0.00000f, 0.00000f, 1.00000f, -0.10106f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
                bindposesMatrices.Add("RightWrist", new Matrix4x4(
                    new Vector4(1.00000f, 0.00000f, 0.00000f, -0.54411f),
                    new Vector4(0.00000f, 1.00000f, 0.00000f, -0.46030f),
                    new Vector4(0.00000f, 0.00000f, 1.00000f, -0.09431f),
                    new Vector4(0.00000f, 0.00000f, 0.00000f, 1.00000f)));
            }
            return bindposesMatrices.SafeGet(name, Matrix4x4.identity).transpose;
        }
    }
}
