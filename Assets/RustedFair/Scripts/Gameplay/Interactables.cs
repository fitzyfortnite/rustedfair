using System;
using System.Collections.Generic;
using UnityEngine;

namespace RustedFair
{
    public struct InteractInfo
    {
        public string text;      // already localised
        public float hold;       // seconds; 0 = instant
        public bool enabled;
        public string icon;

        public static InteractInfo Make(string text, bool enabled = true, float hold = 0f, string icon = "icon_hand")
            => new InteractInfo { text = text, enabled = enabled, hold = hold, icon = icon };
    }

    public abstract class Interactable : MonoBehaviour
    {
        public abstract InteractInfo Query(PlayerController p);
        public abstract void Interact(PlayerController p);
        public virtual void OnDenied(PlayerController p)
        {
            if (AudioManager.I != null) AudioManager.I.PlayAt("door_locked", transform.position, 0.6f, 1f, 8f);
        }
        public virtual void OnHoldTick(PlayerController p, float t01) { }
        public virtual void OnHoldCancel(PlayerController p) { }

        /// <summary>Adds an interaction collider (trigger-less box on the Interact layer).</summary>
        public BoxCollider AddHitBox(Vector3 localCenter, Vector3 size)
        {
            var go = new GameObject("hit");
            go.transform.SetParent(transform, false);
            go.layer = RF.LayerInteract;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = localCenter;
            bc.size = size;
            bc.isTrigger = true; // still raycast-able with QueryTriggerInteraction.Collide
            return bc;
        }
    }

    // ===================================================================== Pickup
    public class Pickup : Interactable
    {
        /// <summary>Every pickup still lying in the world (used by the hint director).</summary>
        public static readonly List<Pickup> All = new List<Pickup>();
        public ItemId item;
        public int amount = 1;
        /// <summary>Hint flare end time (Time.time); see ItemGlint.</summary>
        public float hintUntil = -1f;
        public Action<PlayerController> onTaken;
        public bool bob = true;
        Vector3 basePos;
        float phase;

        void Start()
        {
            basePos = transform.localPosition;
            phase = UnityEngine.Random.value * 10f;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>The drawer this pickup is hidden in (null if it lies in the open).</summary>
        public Drawer InDrawer => GetComponentInParent<Drawer>();

        void Update()
        {
            if (!bob) return;
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin((Time.time + phase) * 2f) * 0.02f);
            transform.Rotate(0f, 18f * Time.deltaTime, 0f, Space.World);
        }

        public override InteractInfo Query(PlayerController p)
        {
            bool full = !p.Inventory.CanAdd(item);
            string name = Loc.T(Items.NameKey(item));
            if (full) return InteractInfo.Make(Loc.F("int_full", name), false);
            return InteractInfo.Make(Loc.F("int_take", name), true, 0f, Items.Icon(item));
        }

        public override void OnDenied(PlayerController p) { }

        public override void Interact(PlayerController p)
        {
            if (item == ItemId.Battery && p.Flashlight != null && p.Flashlight.Charge < 0.95f && p.Inventory.Count(ItemId.Battery) == 0)
            {
                p.Flashlight.AddCharge(0.5f);
            }
            else if (!p.Inventory.Add(item, amount)) return;
            if (AudioManager.I != null) AudioManager.I.PlayAt("pickup", transform.position, 0.8f, 1f, 6f);
            GameEvents.ItemPicked(item);
            onTaken?.Invoke(p);
            Destroy(gameObject);
        }
    }

    // ===================================================================== door sounds by material
    public enum DoorSurface { Wood, Metal, Cloth }

    /// <summary>v1.0.3: doors, cabinets and lids sound like what they are made of.</summary>
    public static class DoorSounds
    {
        public static DoorSurface FromMaterial(string mat)
        {
            if (string.IsNullOrEmpty(mat)) return DoorSurface.Wood;
            mat = mat.ToLowerInvariant();
            if (mat.Contains("tent") || mat.Contains("fabric") || mat.Contains("cloth")) return DoorSurface.Cloth;
            if (mat.Contains("wood") || mat.Contains("cardboard")) return DoorSurface.Wood;
            if (mat.Contains("metal") || mat.Contains("rust") || mat.Contains("grate") || mat.Contains("chrome") || mat.Contains("paint"))
                return DoorSurface.Metal; // painted panels in the fair are steel (lockers, service doors)
            return DoorSurface.Wood;
        }

        public static DoorSurface FromRenderer(Transform t)
        {
            var r = t != null ? t.GetComponentInChildren<Renderer>() : null;
            return r != null && r.sharedMaterial != null ? FromMaterial(r.sharedMaterial.name) : DoorSurface.Wood;
        }

        /// <summary>action: open / close / slam / locked.</summary>
        public static string Clip(string action, DoorSurface s)
        {
            if (s == DoorSurface.Cloth) return action == "locked" ? "door_locked_wood" : "curtain";
            return "door_" + action + (s == DoorSurface.Metal ? "_metal" : "_wood");
        }
    }

    // ===================================================================== Door
    public class Door : Interactable
    {
        public DoorSurface surface = DoorSurface.Wood;

        public override void OnDenied(PlayerController p)
        {
            if (AudioManager.I != null) AudioManager.I.PlayAt(DoorSounds.Clip("locked", surface), transform.position + Vector3.up, 0.7f, 1f, 8f);
        }

        public Transform hinge;
        public float openAngle = 100f;
        public ItemId keyItem = ItemId.None;
        public Func<bool> unlockCondition;
        public string lockedKey = "int_locked";
        public bool locked;
        public bool robotsCanUse = true;
        public bool consumeKey;
        public float width = 1.7f;

        public bool IsOpen { get; private set; }
        float targetAngle, angle;
        float speed = 160f;

        /// <summary>Robots cannot path through a locked door.</summary>
        public bool BlocksRobots => locked || !robotsCanUse;

        public override InteractInfo Query(PlayerController p)
        {
            if (locked)
            {
                if (keyItem != ItemId.None && p.Inventory.Has(keyItem))
                    return InteractInfo.Make(Loc.F("int_unlock", Loc.T(Items.NameKey(keyItem))), true, 0f, Items.Icon(keyItem));
                if (unlockCondition != null && unlockCondition())
                    return InteractInfo.Make(Loc.T("int_open"));
                return InteractInfo.Make(Loc.T(lockedKey), false, 0f, "icon_lock");
            }
            return InteractInfo.Make(Loc.T(IsOpen ? "int_close" : "int_open"));
        }

        public override void Interact(PlayerController p)
        {
            if (locked)
            {
                if (keyItem != ItemId.None && p.Inventory.Has(keyItem))
                {
                    if (consumeKey) p.Inventory.Remove(keyItem);
                    Unlock();
                }
                else if (unlockCondition != null && unlockCondition()) Unlock();
                else return;
            }
            if (IsOpen) Close(); else OpenAwayFrom(p.transform.position, false);
        }

        public void Unlock()
        {
            locked = false;
            if (AudioManager.I != null) AudioManager.I.PlayAt("item_insert", transform.position, 0.7f, 1f, 8f);
        }

        public void OpenAwayFrom(Vector3 from, bool violent)
        {
            if (IsOpen) return;
            IsOpen = true;
            Vector3 toFrom = from - transform.position;
            // Euler(0,+a) swings the panel (local +x from the hinge) towards local -z, i.e. away from someone
            // standing on the +forward side. The old sign opened doors INTO the player / robot.
            float side = Vector3.Dot(toFrom, transform.forward) >= 0f ? 1f : -1f;
            targetAngle = openAngle * side;
            speed = violent ? 520f : 170f;
            if (AudioManager.I != null)
            {
                if (violent)
                {
                    AudioManager.I.PlayAt(DoorSounds.Clip("slam", surface), transform.position + Vector3.up, 1f, 3f, 35f);
                    Noise.Emit(transform.position, 14f, NoiseKind.Loud);
                }
                else AudioManager.I.PlayAt(DoorSounds.Clip("open", surface), transform.position + Vector3.up, 0.8f, 2f, 18f);
            }
            if (!violent) Noise.Emit(transform.position, 6f, NoiseKind.Footstep);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            targetAngle = 0f;
            speed = 170f;
            if (AudioManager.I != null) AudioManager.I.PlayAt(DoorSounds.Clip("close", surface), transform.position + Vector3.up, 0.8f, 2f, 18f);
            Noise.Emit(transform.position, 7f, NoiseKind.Footstep);
        }

        void Update()
        {
            if (Mathf.Approximately(angle, targetAngle)) return;
            angle = Mathf.MoveTowards(angle, targetAngle, speed * Time.deltaTime);
            if (hinge != null) hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>Builds a hinged door at an edge. forward = the edge normal.</summary>
        public static Door Create(Transform parent, Vector3 center, Vector3 forward, float width, float height, string mat, string name = "Door")
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var door = root.AddComponent<Door>();
            door.width = width;
            door.surface = DoorSounds.FromMaterial(mat);

            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
            door.hinge = hinge;

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(hinge, false);
            panel.transform.localPosition = new Vector3(width * 0.5f, height * 0.5f, 0f);
            panel.transform.localScale = new Vector3(width - 0.04f, height - 0.02f, 0.07f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = MatLib.Get(mat);
            panel.layer = RF.LayerDefault;

            // handle
            var handle = RF.Prim(PrimitiveType.Cube, panel.transform, "chrome", new Vector3(0.4f, -0.02f, 0f), new Vector3(0.06f, 0.025f, 2.2f), default, false);
            handle.name = "Handle";
            return door;
        }
    }

    // ===================================================================== Drawer
    public class Drawer : Interactable
    {
        public Transform slide;
        public Vector3 openOffset = new Vector3(0, 0, 0.38f);
        public Pickup contents;
        bool open;
        public bool IsOpen => open;
        Vector3 closedPos;
        float t;

        void Start()
        {
            if (slide != null) closedPos = slide.localPosition;
            if (contents != null) SetContentsReachable(false);
        }

        void SetContentsReachable(bool on)
        {
            if (contents == null) return;
            foreach (var c in contents.GetComponentsInChildren<Collider>()) c.enabled = on;
        }

        public override InteractInfo Query(PlayerController p) =>
            InteractInfo.Make(Loc.T(open ? "int_close" : "int_search"));

        public override void Interact(PlayerController p)
        {
            open = !open;
            if (AudioManager.I != null) AudioManager.I.PlayAt("locker_open", transform.position, 0.5f, 1f, 10f, 0.1f, 1.4f);
            Noise.Emit(transform.position, 5f, NoiseKind.Footstep);
            if (open && contents != null) SetContentsReachable(true);
        }

        void Update()
        {
            if (slide == null) return;
            t = Mathf.MoveTowards(t, open ? 1f : 0f, Time.deltaTime * 3f);
            slide.localPosition = closedPos + openOffset * Mathf.SmoothStep(0, 1, t);
        }
    }

    // ===================================================================== ItemSocket
    /// <summary>Insert N items of a type (fuses, tokens, gears...). Calls onComplete when full.</summary>
    public class ItemSocket : Interactable
    {
        public ItemId item;
        public int required = 1;
        public bool needsPower;
        public string promptKey = "int_insert";
        public string doneKey = "int_done";
        public List<GameObject> fillVisuals = new List<GameObject>();
        public Action onComplete;
        public Action<int> onInserted;
        public int Inserted { get; private set; }
        public bool Complete => Inserted >= required;
        public Func<bool> extraCondition;
        public string extraConditionKey;

        public override InteractInfo Query(PlayerController p)
        {
            string itemName = Loc.T(Items.NameKey(item));
            if (Complete) return InteractInfo.Make(Loc.T(doneKey), false);
            if (needsPower && !Power.On) return InteractInfo.Make(Loc.T("int_nopower"), false, 0f, "icon_lock");
            if (extraCondition != null && !extraCondition()) return InteractInfo.Make(Loc.T(extraConditionKey), false, 0f, "icon_lock");
            if (!p.Inventory.Has(item))
                return InteractInfo.Make(Loc.F("int_needs", itemName, Inserted, required), false, 0f, Items.Icon(item));
            return InteractInfo.Make(Loc.F(promptKey, itemName, Inserted, required), true, 0f, Items.Icon(item));
        }

        public override void OnDenied(PlayerController p)
        {
            if (AudioManager.I != null) AudioManager.I.PlayAt("keypad_error", transform.position, 0.5f, 1f, 6f);
        }

        public override void Interact(PlayerController p)
        {
            int n = Mathf.Min(p.Inventory.Count(item), required - Inserted);
            if (n <= 0) return;
            p.Inventory.Remove(item, n);
            for (int i = 0; i < n; i++)
            {
                if (Inserted < fillVisuals.Count && fillVisuals[Inserted] != null) fillVisuals[Inserted].SetActive(true);
                Inserted++;
            }
            if (AudioManager.I != null) AudioManager.I.PlayAt(item == ItemId.Gear ? "gear_place" : "item_insert", transform.position, 0.9f, 1.5f, 12f);
            onInserted?.Invoke(Inserted);
            if (Complete) onComplete?.Invoke();
        }
    }

    // ===================================================================== HoldAction
    /// <summary>Hold to perform (cranking, smashing ice, pulling the generator lever). Noisy while held.</summary>
    public class HoldAction : Interactable
    {
        public string promptKey;
        public float seconds = 3f;
        public float noiseRadius = 18f;
        public string loopSound;
        public Func<bool> condition;
        public Func<string> deniedText;
        public Action onDone;
        public bool done;
        public Action<float> onProgress;
        AudioSource loop;
        float noiseTimer;

        public override InteractInfo Query(PlayerController p)
        {
            if (done) return InteractInfo.Make(Loc.T("int_done"), false);
            if (condition != null && !condition())
                return InteractInfo.Make(deniedText != null ? deniedText() : Loc.T("int_locked"), false, 0f, "icon_lock");
            return InteractInfo.Make(Loc.T(promptKey), true, seconds);
        }

        public override void OnHoldTick(PlayerController p, float t01)
        {
            if (loop == null && !string.IsNullOrEmpty(loopSound))
            {
                loop = AudioManager.Attach(gameObject, loopSound, 0.9f, true, 2f, 30f);
            }
            noiseTimer -= Time.deltaTime;
            if (noiseTimer <= 0f)
            {
                noiseTimer = 0.6f;
                Noise.Emit(transform.position, noiseRadius, NoiseKind.Loud);
            }
            onProgress?.Invoke(t01);
        }

        public override void OnHoldCancel(PlayerController p) => StopLoop();

        void StopLoop()
        {
            if (loop != null) { Destroy(loop); loop = null; }
        }

        public override void Interact(PlayerController p)
        {
            StopLoop();
            if (done) return;
            done = true;
            onDone?.Invoke();
        }
    }

    // ===================================================================== Generic use
    public class UseAction : Interactable
    {
        public Func<PlayerController, InteractInfo> query;
        public Action<PlayerController> use;
        public Action<PlayerController> denied;

        public override InteractInfo Query(PlayerController p) => query != null ? query(p) : InteractInfo.Make(Loc.T("int_use"));
        public override void Interact(PlayerController p) => use?.Invoke(p);
        public override void OnDenied(PlayerController p)
        {
            if (denied != null) denied(p); else base.OnDenied(p);
        }
    }

    // ===================================================================== Notes
    public class NotePickup : Interactable
    {
        /// <summary>Notes spawned but not read yet (used by the hint director).</summary>
        public static readonly List<NotePickup> All = new List<NotePickup>();
        public int index; // 0..6

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public override InteractInfo Query(PlayerController p) => InteractInfo.Make(Loc.T("int_read_note"), true, 0f, "icon_notes");

        public override void Interact(PlayerController p)
        {
            if (AudioManager.I != null) AudioManager.I.Play2D("ui_note", 0.9f);
            if (GameController.I != null) GameController.I.Quest.NoteFound(index);
            Destroy(gameObject);
        }

        public static NotePickup Spawn(Transform parent, Vector3 pos, float yaw, int index)
        {
            var go = new GameObject("Note_" + (index + 1));
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var paper = RF.Prim(PrimitiveType.Quad, go.transform, "paper", new Vector3(0, 0.01f, 0), new Vector3(0.24f, 0.3f, 1f), new Vector3(90, 0, 0), false);
            paper.name = "Paper";
            // faint glow so the note is findable in the dark
            var l = new GameObject("Glint").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = new Vector3(0, 0.35f, 0);
            l.type = LightType.Point;
            l.range = 1.2f;
            l.intensity = 0.6f;
            l.color = new Color(1f, 0.85f, 0.6f);
            l.shadows = LightShadows.None;
            var n = go.AddComponent<NotePickup>();
            n.index = index;
            n.AddHitBox(new Vector3(0, 0.08f, 0), new Vector3(0.45f, 0.25f, 0.45f));
            return n;
        }
    }

    /// <summary>Simple static game events used by achievements, tips and the HUD.</summary>
    public static class GameEvents
    {
        public static event Action<ItemId> OnItemPicked;
        public static void ItemPicked(ItemId id) => OnItemPicked?.Invoke(id);
        public static void Clear() { OnItemPicked = null; }
    }
}
