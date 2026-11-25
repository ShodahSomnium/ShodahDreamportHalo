using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Another Dreamport player as every module sees it (HaloNetGame.Players): refreshed once a frame from their record; the pose is the interpolated one
    /// (drawn 0.16 s behind the record). One object per slot for as long as the slot is held by the same player. Read only for other modules.
    /// </summary>
    public sealed class HaloNetPlayer
    {
        /// <summary>The net player id (slot 0..49) and the Fusion / Bridge player id (PhotonId).</summary>
        public int Slot, PlayerId;
        public string Name = "";
        /// <summary>The instance master; in my match (same map, same game); alive; the other side's player (for the HUD's colours).</summary>
        public bool IsMaster, InMatch, Alive, Enemy;
        /// <summary>Their side 0 red / 1 blue (HaloTeams.Red / Blue); in a free-for-all game every other player is an enemy (see <see cref="Enemy"/>).</summary>
        public int Team;
        public int Kills, Deaths, Assists, Score, LivesLeft = -1;
        /// <summary>The record's status (Present in the hub, Playing a match).</summary>
        public HaloNetStatus Status;
        /// <summary>Their event counters as the record carries them (reload 4 bits, melee 4 bits, throw 4 bits): +1 per event (the Desk module's posing).</summary>
        public int Reloads, Melees, Throws;
        /// <summary>The objective they carry: 0 red flag, 1 blue flag, 2 oddball, -1 none (= Carry - 1).</summary>
        public int Carrying { get { return Carry - 1; } }
        /// <summary>The carry code on the wire (0 none, 1 red flag, 2 blue flag, 3 oddball).</summary>
        public int Carry;
        /// <summary>0..1 and 0..3 (over shield above 1), as the writer last said.</summary>
        public float Health, Shield;
        public bool Camo, Vr, Zoomed, Crouch, Reloading, InHand;
        /// <summary>Feet (the avatar root), the head, the yaw of the body (degrees), the head's rotation: the interpolated pose in Unity world space.</summary>
        public Vector3 Position, Head;
        public Quaternion HeadRot = Quaternion.identity;
        public float Yaw;
        public Vector3 Forward = Vector3.forward;
        /// <summary>The hands' poses in the aim frame (VR writers; a monitor writer's hands are placed in front of the camera).</summary>
        public Vector3 LeftHand, RightHand;
        public Quaternion LeftHandRot = Quaternion.identity, RightHandRot = Quaternion.identity;
        /// <summary>Metres per second, measured from the drawn pose.</summary>
        public Vector3 Velocity;
        /// <summary>Halo's motion tracker shows a player who moves faster than a crawl or who fires (see <see cref="FiredRecently"/>).</summary>
        public bool Moving;
        /// <summary>Fired within the last 0.5 s (motion tracker; Time.time of the last shot in <see cref="LastShotAt"/>).</summary>
        public bool FiredRecently;
        public float LastShotAt = -100f;
        /// <summary>The weapon in hand (or the primary one when both are on the body) and the other weapon; null when none (tags of the current map).</summary>
        public HaloTag HeldWeapon, StowedWeapon;
        public int Frags, Plasmas;
        /// <summary>The vehicle (scenario vehicle index) and seat they sit in, -1 on foot; Driving = they drive it (its pose comes from their record).</summary>
        public int VehicleIndex = -1, Seat = -1;
        public bool Driving;
        /// <summary>The aim direction they publish (vehicle gun / turret).</summary>
        public Vector3 Aim = Vector3.forward;
        /// <summary>Their Somnium avatar's humanoid Animator (found near their head; null until found) and the Bridge body root (null when unknown).</summary>
        public Animator Avatar;
        public Transform Root;
        /// <summary>The Bridge's head of their body as Somnium draws it (null when unknown): the avatar is found and checked against it.</summary>
        public Transform BridgeHead;
        /// <summary>Time.time of the record's last change.</summary>
        public float Seen;

        public override string ToString() { return Name + " (slot " + Slot + ", player " + PlayerId + ")"; }
    }

    /// <summary>
    /// A remote player's body as the weapons and hit boxes need it (the same shape HaloBody gives the local player: head, torso frame, hips, shoulders, feet),
    /// rebuilt every frame from the interpolated pose they sent. A monitor writer sends no hands: they are placed where HaloBody places a monitor player's.
    /// Nothing here touches the Somnium avatar (drawn by Somnium). Bo2NetBody ported.
    /// </summary>
    public sealed class HaloNetBody
    {
        public Vector3 Up = Vector3.up, Right = Vector3.right, Forward = Vector3.forward;
        public Vector3 Chest, Hips, ShoulderL, ShoulderR, Feet, HeadPos;
        public Quaternion HeadRot = Quaternion.identity;
        public float Scale = 1f;
        public bool Valid, Vr;
        public Vector3 LHandPos, RHandPos;
        public Quaternion LHandRot = Quaternion.identity, RHandRot = Quaternion.identity;
        public float Yaw;

        public void Update(in HaloNetTimeline.Sample s, float zoom)
        {
            Feet = HaloNetU.V(s.Root);
            HeadPos = HaloNetU.V(s.Head);
            HeadRot = HaloNetU.Q(s.HeadRot);
            Yaw = s.RootYaw;
            Up = Vector3.up;
            Forward = Quaternion.Euler(0f, s.RootYaw, 0f) * Vector3.forward;
            Right = Vector3.Cross(Up, Forward);
            float height = Mathf.Max(0.8f, HeadPos.y - Feet.y);
            Scale = Mathf.Clamp(height / 1.7f, 0.6f, 1.5f);
            Vector3 neck = HeadPos - Up * (0.2f * Scale);
            Vector3 spine = neck - Forward * (0.05f * Scale);
            Chest = spine - Up * (0.2f * Scale);
            Hips = new Vector3(spine.x, Feet.y + height * 0.55f, spine.z);
            ShoulderL = Chest + Up * (0.1f * Scale) - Right * (0.18f * Scale);
            ShoulderR = Chest + Up * (0.1f * Scale) + Right * (0.18f * Scale);
            Vr = s.Vr;
            if (Vr)
            {
                LHandPos = HaloNetU.V(s.LHand);
                RHandPos = HaloNetU.V(s.RHand);
                LHandRot = HaloNetU.Q(s.LRot);
                RHandRot = HaloNetU.Q(s.RRot);
            }
            else
            {
                // HaloBody.HandPose, monitor branch
                Quaternion look = HeadRot;
                Vector3 hip = HeadPos + look * HaloBody.DesktopHip;
                Vector3 aim = HeadPos + look * new Vector3(0.0f, -0.07f, 0.42f);
                RHandPos = Vector3.Lerp(hip, aim, Mathf.Clamp01(zoom));
                RHandRot = look;
                LHandPos = HeadPos + look * new Vector3(-0.06f, -0.2f, 0.62f);
                LHandRot = look;
            }

            Valid = true;
        }

        /// <summary>A guess at the elbow: half way from the shoulder to the hand, dropped and pushed out a little.</summary>
        public Vector3 Elbow(Vector3 shoulder, Vector3 hand, float side)
        {
            return (shoulder + hand) * 0.5f - Up * (0.1f * Scale) + Right * (side * 0.06f * Scale);
        }
    }
}
