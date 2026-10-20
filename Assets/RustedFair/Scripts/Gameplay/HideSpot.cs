using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public enum HideKind { Locker, Booth, Coffin, Cabinet, Wardrobe, Crate }

    /// <summary>
    /// A place to hide. Robots that saw the player climb in will drag them out; Brutus also
    /// checks hiding spots at random while patrolling.
    /// </summary>
    public class HideSpot : Interactable
    {
        public static readonly List<HideSpot> All = new List<HideSpot>();

        public HideKind kind;
        public Transform viewPoint;   // camera position/orientation while hidden
        public Transform exitPoint;   // where the player stands after leaving
        public Transform doorHinge;   // optional animated door
        public float doorOpenAngle = -100f;
        public readonly List<Renderer> occluders = new List<Renderer>(); // hidden while the camera is inside
        public PlayerController Occupant { get; private set; }
        public float LastEnterTime { get; private set; } = -999f;
        public Vector3 lastEnterPlayerPos;

        float doorT, doorTarget;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public override InteractInfo Query(PlayerController p)
        {
            if (Occupant != null) return InteractInfo.Make(Loc.T("int_leave"), true, 0f, "icon_eye");
            return InteractInfo.Make(Loc.T("int_hide"), true, 0f, "icon_hide");
        }

        public override void Interact(PlayerController p)
        {
            if (Occupant == null) p.EnterHide(this);
            else p.ExitHide();
        }

        public void SetOccupant(PlayerController p)
        {
            Occupant = p;
            if (p != null)
            {
                LastEnterTime = Time.time;
                lastEnterPlayerPos = p.transform.position;
            }
        }

        public void AnimateDoor(bool open)
        {
            doorTarget = open ? 1f : 0f;
            if (AudioManager.I != null)
            {
                // v1.0.3: by material - steel lockers/cabinets clang, wooden cabinets/coffins/wardrobes knock, curtains swish
                var surf = kind == HideKind.Locker ? DoorSurface.Metal : (kind == HideKind.Crate || doorHinge == null ? DoorSurface.Wood : DoorSounds.FromRenderer(doorHinge));
                string snd = surf == DoorSurface.Metal ? (open ? "locker_open" : "locker_close") : DoorSounds.Clip(open ? "open" : "close", surf);
                // smaller panels than a room door: a little higher and quieter
                float pitch = surf == DoorSurface.Wood ? 1.18f : 1f;
                AudioManager.I.PlayAt(snd, transform.position + Vector3.up, 0.7f, 1.5f, 14f, 0.06f, pitch);
            }
            Noise.Emit(transform.position, 5f, NoiseKind.Footstep);
        }

        void Update()
        {
            if (doorHinge == null) return;
            if (Mathf.Approximately(doorT, doorTarget)) return;
            doorT = Mathf.MoveTowards(doorT, doorTarget, Time.deltaTime * 4f);
            doorHinge.localRotation = Quaternion.Euler(0f, doorOpenAngle * Mathf.SmoothStep(0, 1, doorT), 0f);
        }

        /// <summary>Plays the "open, peek, close" when a robot checks the spot.</summary>
        public void RobotCheck()
        {
            if (IsInvoking(nameof(CloseAfterCheck))) return; // already peeking (avoid per-frame sound/noise spam)
            AnimateDoor(true);
            Invoke(nameof(CloseAfterCheck), 1.2f);
        }

        void CloseAfterCheck() { if (Occupant == null) AnimateDoor(false); }

        // ---------------------------------------------------------------- builders
        /// <summary>Metal locker. forward = direction the locker faces (towards the room).</summary>
        public static HideSpot Locker(Transform parent, Vector3 pos, float yaw, string mat = "wall_metal")
        {
            var root = new GameObject("Locker");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            float w = 0.8f, h = 2.05f, d = 0.65f;

            // body: back, sides, top, bottom (real colliders so the player can't walk through)
            AddSolid(root.transform, mat, new Vector3(0, h * 0.5f, -d * 0.5f + 0.02f), new Vector3(w, h, 0.04f));
            AddSolid(root.transform, mat, new Vector3(-w * 0.5f + 0.02f, h * 0.5f, 0), new Vector3(0.04f, h, d));
            AddSolid(root.transform, mat, new Vector3(w * 0.5f - 0.02f, h * 0.5f, 0), new Vector3(0.04f, h, d));
            AddSolid(root.transform, mat, new Vector3(0, h - 0.02f, 0), new Vector3(w, 0.04f, d));
            AddSolid(root.transform, mat, new Vector3(0, 0.05f, 0), new Vector3(w, 0.1f, d));

            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(-w * 0.5f, 0, d * 0.5f);
            var door = RF.Prim(PrimitiveType.Cube, hinge, mat, new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(w - 0.02f, h - 0.04f, 0.035f));
            door.name = "LockerDoor";
            // vents
            for (int i = 0; i < 5; i++)
                RF.Prim(PrimitiveType.Cube, door.transform, "black_matte", new Vector3(0, 0.36f - i * 0.035f, 0.55f), new Vector3(0.7f, 0.012f, 0.2f), default, false);
            RF.Prim(PrimitiveType.Cube, door.transform, "chrome", new Vector3(0.38f, 0f, 0.8f), new Vector3(0.04f, 0.06f, 1f), default, false);

            var hs = Finish(root, HideKind.Locker, hinge, new Vector3(0, 1.58f, 0.02f), new Vector3(0, 0, 1.0f), new Vector3(w, h, d));
            hs.occluders.AddRange(door.GetComponentsInChildren<Renderer>());
            return hs;
        }

        /// <summary>Generic hide spot built around a custom visual (photo booth, coffin, wardrobe...).</summary>
        public static HideSpot Custom(Transform parent, string name, Vector3 pos, float yaw, HideKind kind, Vector3 viewLocal, Vector3 exitLocal, Vector3 hitSize, Transform doorHinge = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (doorHinge != null) doorHinge.SetParent(root.transform, true);
            return Finish(root, kind, doorHinge, viewLocal, exitLocal, hitSize);
        }

        static HideSpot Finish(GameObject root, HideKind kind, Transform hinge, Vector3 viewLocal, Vector3 exitLocal, Vector3 hitSize)
        {
            var hs = root.AddComponent<HideSpot>();
            hs.kind = kind;
            hs.doorHinge = hinge;
            hs.viewPoint = RF.Child(root.transform, "View", viewLocal).transform;
            hs.exitPoint = RF.Child(root.transform, "Exit", exitLocal).transform;
            hs.AddHitBox(new Vector3(0, hitSize.y * 0.5f, 0), hitSize + new Vector3(0.1f, 0, 0.2f));
            return hs;
        }

        public static GameObject AddSolid(Transform parent, string mat, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = MatLib.Get(mat);
            go.layer = RF.LayerProps;
            return go;
        }
    }
}
