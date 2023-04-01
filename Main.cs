﻿using System.Collections.Generic;
using System;
using UnityEngine;
using MelonLoader;
using RootMotion.FinalIK;
using ABI_RC.Systems.IK.SubSystems;
using ABI_RC.Systems.MovementSystem;
using ABI_RC.Core.Savior;
using System.Reflection;
using ABI_RC.Core.Util.Object_Behaviour;

[assembly: MelonGame("Alpha Blend Interactive", "ChilloutVR")]
[assembly: MelonInfo(typeof(Koneko.LimbGrabber), "LimbGrabber", "1.0.0", "Exterrata")]

namespace Koneko;
public class LimbGrabber : MelonMod
{
    public static readonly MelonPreferences_Category Category = MelonPreferences.CreateCategory("LimbGrabber");
    public static readonly MelonPreferences_Entry<bool> Enabled = Category.CreateEntry<bool>("Enabled", true);
    public static readonly MelonPreferences_Entry<bool> EnableHands = Category.CreateEntry<bool>("EnableHands", true);
    public static readonly MelonPreferences_Entry<bool> EnableFeet = Category.CreateEntry<bool>("EnableFeet", true);
    public static readonly MelonPreferences_Entry<bool> EnableHead = Category.CreateEntry<bool>("EnableHead", true);
    public static readonly MelonPreferences_Entry<bool> EnableHip = Category.CreateEntry<bool>("EnableHip", true);
    public static readonly MelonPreferences_Entry<bool> EnableRoot = Category.CreateEntry<bool>("EnableRoot", true);
    public static readonly MelonPreferences_Entry<bool> PreserveMomentum = Category.CreateEntry<bool>("PreserveMomentum", true);
    public static readonly MelonPreferences_Entry<bool> CameraFollow = Category.CreateEntry<bool>("CameraFollowHead", false);
    public static readonly MelonPreferences_Entry<bool> Friend = Category.CreateEntry<bool>("FriendsOnly", true);
    public static readonly MelonPreferences_Entry<bool> Debug = Category.CreateEntry<bool>("Debug", false);
    public static readonly MelonPreferences_Entry<float> VelocityMultiplier = Category.CreateEntry<float>("VelocityMultiplier", 1f);
    public static readonly MelonPreferences_Entry<float> GravityMultiplier = Category.CreateEntry<float>("GravityMultiplier", 1f);
    public static readonly MelonPreferences_Entry<float> Distance = Category.CreateEntry<float>("Distance", 0.15f);
    //  LeftHand = 0
    //  LeftFoot = 1
    //  RightHand = 2
    //  RightFoot = 3
    //  Head = 4
    //  Hip = 5
    //  Root = 6
    public static readonly string[] LimbNames = { "LeftHand", "LeftFoot", "RightHand", "RightFoot", "Head", "Hip"};
    public static MelonPreferences_Entry<bool>[] enabled;
    public static bool[] tracking;
    public static Limb[] Limbs;
    public static Dictionary<int, Grabber> Grabbers;
    public static Transform PlayerLocal;
    public static Vector3 RootOffset;
    public static Vector3 LastRootPosition;
    public static Vector3 Velocity;
    public static Vector3[] AverageVelocities;
    public static int VelocityIndex;
    public static GameObject Camera;
    public static IKSolverVR IKSolver;
    public static FieldInfo Grounded;
    public static bool Initialized;
    public static bool IsAirborn;
    public static int count = 0;

    public struct Limb
    {
        public Transform limb;
        public Transform Parent;
        public Transform Target;
        public Transform PreviousTarget;
        public Quaternion RotationOffset;
        public Vector3 PositionOffset;
        public bool Grabbed;
    }

    public class Grabber
    {
        public Transform grabber;
        public int limb;
        public Grabber(Transform grabber, bool grabbing, int limb)
        {
            this.grabber = grabber;
            this.limb = limb;
        }
    }

    public override void OnInitializeMelon()
    {
        MelonLogger.Msg("Starting");
        tracking = new bool[6];
        Grabbers = new Dictionary<int, Grabber>();
        AverageVelocities = new Vector3[10];
        enabled = new MelonPreferences_Entry<bool>[6] {
            EnableHands,
            EnableFeet,
            EnableHands,
            EnableFeet,
            EnableHead,
            EnableHip
        };
        Patches.grabbing = new Dictionary<int, bool>();
        HarmonyInstance.PatchAll(typeof(Patches));
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (buildIndex == 3)
        {
            Limbs = new Limb[6];
            PlayerLocal = GameObject.Find("_PLAYERLOCAL").transform;
            for (int i = 0; i < Limbs.Length; i++)
            {
                var limb = new GameObject("LimbGrabberTarget").transform;
                Limbs[i].Target = limb;
                limb.parent = PlayerLocal;
                if (i == 4)
                {
                    var camera = new GameObject("Camera");
                    var cameraComponent = camera.AddComponent<Camera>();
                    camera.AddComponent<DisableXRFollow>();
                    camera.transform.parent = limb;
                    camera.SetActive(false);
                    cameraComponent.nearClipPlane = 0.01f;
                    Camera = camera;
                }
            }
            Grounded = typeof(MovementSystem).GetField("_isGroundedRaw", BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }

    public override void OnUpdate()
    {
        if (!Initialized || !Enabled.Value) return;
        if (count == 1000)
        {
            count = 0;
            List<int> remove = new List<int>();
            foreach (KeyValuePair<int, Grabber> grabber in Grabbers)
            {
                if (grabber.Value.grabber == null) remove.Add(grabber.Key);
            }
            foreach (int key in remove)
            {
                Grabbers.Remove(key);
            }
        }
        count++;
        for (int i = 0; i < Limbs.Length; i++)
        {
            if (Limbs[i].Grabbed && Limbs[i].Parent != null)
            {
                Vector3 offset = Limbs[i].Parent.rotation * Limbs[i].PositionOffset;
                Limbs[i].Target.position = Limbs[i].Parent.position + offset;
                Limbs[i].Target.rotation = Limbs[i].Parent.rotation * Limbs[i].RotationOffset;
                if(i == 4) IsAirborn = true;
            }
        }
        if (EnableRoot.Value && Limbs[4].Parent != null)
        {
            if (PreserveMomentum.Value)
            {
                AverageVelocities[VelocityIndex] = PlayerLocal.position - LastRootPosition;
                LastRootPosition = PlayerLocal.position;
                VelocityIndex++;
                if (VelocityIndex == AverageVelocities.Length)
                {
                    VelocityIndex = 0;
                }
            }
            if (Limbs[4].Grabbed) PlayerLocal.position = Limbs[4].Parent.position + RootOffset;
            else if (IsAirborn)
            {
                if (Physics.CheckSphere(PlayerLocal.position, 0.1f, MovementSystem.Instance.groundMask, QueryTriggerInteraction.Ignore))
                {
                    if(Debug.Value) MelonLogger.Msg("Landed");
                    IsAirborn = false;
                    MovementSystem.Instance.canMove = true;
                }
                if (PreserveMomentum.Value)
                {
                    Velocity.y -= MovementSystem.Instance.gravity * 0.01f * Time.deltaTime * GravityMultiplier.Value;
                    PlayerLocal.position += Velocity * VelocityMultiplier.Value;
                }
            }
        }
    }

    public static void Grab(int id, Transform parent)
    {
        if (!Enabled.Value) return;
        if (Debug.Value) MelonLogger.Msg("grab was detected");
        int closest = 0;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < 6; i++)
        {
            float dist = Vector3.Distance(parent.position, Limbs[i].limb.position);
            if (dist < distance)
            {
                distance = dist;
                closest = i;
            }
        }
        if (distance < Distance.Value)
        {
            if (!enabled[closest].Value) return;
            Grabbers[id].limb = closest;
            if (Debug.Value) MelonLogger.Msg("limb " + Limbs[closest].limb.name + " was grabbed by " + parent.name);
            Limbs[closest].PositionOffset = Quaternion.Inverse(parent.rotation) * (Limbs[closest].limb.position - parent.position);
            Limbs[closest].RotationOffset = Quaternion.Inverse(parent.rotation) * Limbs[closest].limb.rotation;
            Limbs[closest].Parent = parent;
            Limbs[closest].Grabbed = true;
            SetTarget(closest, Limbs[closest].Target);
            SetTracking(closest, true);
            if(closest == 4)
            {
                RootOffset = PlayerLocal.position - parent.position;
                if (EnableRoot.Value) MovementSystem.Instance.canMove = false;
                if (CameraFollow.Value)
                {
                    Camera.transform.position = MovementSystem.Instance.rotationPivot.position;
                    Camera.transform.rotation = MovementSystem.Instance.rotationPivot.rotation;
                    MovementSystem.Instance.rotationPivot.gameObject.SetActive(false);
                    Camera.SetActive(true);
                }
            }
        }
    }

    public static void Release(int id)
    {
        int grabber = Grabbers[id].limb;
        if (grabber == -1) return;
        if (Debug.Value) MelonLogger.Msg("limb " + Limbs[grabber].limb.name + " was released by " + Grabbers[id].grabber.name);
        Limbs[grabber].Grabbed = false;
        SetTarget(grabber, Limbs[grabber].PreviousTarget);
        if (!tracking[grabber]) SetTracking(grabber, false);
        if(grabber == 4)
        {
            MovementSystem.Instance.rotationPivot.gameObject.SetActive(true);
            Camera.SetActive(false);
            if (!PreserveMomentum.Value) MovementSystem.Instance.canMove = true;
            else
            {
                for (int i = 0; i < AverageVelocities.Length; i++)
                {
                    Velocity += AverageVelocities[i];
                }
                Velocity /= AverageVelocities.Length;
            }
        }
    }

    public static void SetTarget(int index, Transform Target)
    {
        switch (index)
        {
            case 0:
                IKSolver.leftArm.target = Target;
                break;
            case 1:
                IKSolver.leftLeg.target = Target;
                break;
            case 2:
                IKSolver.rightArm.target = Target;
                break;
            case 3:
                IKSolver.rightLeg.target = Target;
                break;
            case 4:
                IKSolver.spine.headTarget = Target;
                break;
            case 5:
                IKSolver.spine.pelvisTarget = Target;
                break;
        }
    }

    public static void SetTracking(int index, bool value)
    {
        switch (index)
        {
            case 0:
                BodySystem.TrackingLeftArmEnabled = value;
                break;
            case 1:
                BodySystem.TrackingLeftLegEnabled = value;
                break;
            case 2:
                BodySystem.TrackingRightArmEnabled = value;
                break;
            case 3:
                BodySystem.TrackingRightLegEnabled = value;
                break;
            case 4:
                IKSolver.spine.positionWeight = value ? 1 : 0;
                break;
            case 5:
                IKSolver.spine.pelvisPositionWeight = value ? 1 : 0;
                break;
        }
    }
}