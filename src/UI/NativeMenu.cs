using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // The Multiplayer page in MiSide's own main menu, built from the game's own pieces: a MULTIPLAYER button cloned
    // from "Settings" (inserted above Exit), and a page cloned from "Location MainOptions" whose rows are clones of its
    // buttons. Rows: host / stop hosting / leave, every Steam friend hosting, every LAN game, join by address (opens
    // the F5 panel) and the page's own Back. Same font, sounds, hover colours and keyboard navigation as the game.
    // Layout facts from the menu dump: 500x50 buttons, 55 px apart, page title at y -30, first row at y -100.
    // Template idea (strip Localization_UIText, neutralise MenuNextLocation on cloned rows) also used by
    // QuestionBlock64's MiSide-Multiplayer; implemented independently here.
    public sealed partial class TogetherRuntime
    {
        const float RowStep = 55, FirstRow = -100;
        GameObject menuPage, rowTemplate, menuButton;
        RectTransform pageBack;
        MenuLocation pageLocation;
        readonly List<GameObject> rows = new List<GameObject>();
        string rowsSignature = "";
        float nextMenuRefresh;
        bool menuFailed;
        public bool MenuBuilt { get { return menuPage != null && menuButton != null; } }
        bool MenuListening { get { return menuPage != null && menuPage.activeInHierarchy && !Active; } }
        public int MenuRowCount { get { return rows.Count; } }

        sealed class MenuRow { public string Text; public Action Click; }

        void NativeMenuTick()
        {
            if (ActiveSceneName != "SceneMenu") { menuPage = null; menuButton = null; menuFailed = false; rows.Clear(); rowsSignature = ""; return; }
            if (menuPage == null && !menuFailed && arrivedAt >= 0 && Time.realtimeSinceStartup - arrivedAt > 1.5f) BuildNativeMenu();
            if (menuPage != null && menuPage.activeInHierarchy && Time.realtimeSinceStartup >= nextMenuRefresh)
            {
                nextMenuRefresh = Time.realtimeSinceStartup + 1;
                RefreshRows();
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        void BuildNativeMenu()
        {
            Menu menu = Object.FindObjectOfType<Menu>(true);
            if (menu == null) return;
            Transform main = FindDeep(menu.transform, "Location Menu"), options = FindDeep(menu.transform, "Location MainOptions");
            if (main == null || options == null) { Log("Native menu: pages not found"); menuFailed = true; return; }
            MenuLocation mainLocation = main.GetComponent<MenuLocation>();
            Transform settings = main.Find("Button Settings"), exit = main.Find("Button Exit");
            if (mainLocation == null || mainLocation.objects == null || mainLocation.objects.Count < 4 || settings == null || exit == null) { Log("Native menu: buttons not found"); menuFailed = true; return; }

            // The page: a copy of the options page, renamed and retitled, emptied down to its title and Back.
            GameObject page = Object.Instantiate(options.gameObject, options.parent).Cast<GameObject>();
            page.name = "Location MiSideTogether";
            page.SetActive(false);
            pageLocation = page.GetComponent<MenuLocation>();
            Transform title = page.transform.Find("Text");
            if (title != null) SetText(title.gameObject, "MULTIPLAYER");
            Transform back = page.transform.Find("Button Back");
            pageBack = back != null ? back.GetComponent<RectTransform>() : null;
            Transform template = page.transform.Find("Button Option Game");
            if (template == null || pageBack == null) { Object.Destroy(page); Log("Native menu: options page layout changed"); menuFailed = true; return; }
            rowTemplate = template.gameObject;
            rowTemplate.SetActive(false);
            foreach (string n in new[] { "Button Full Clear", "Button Option Graphics", "Button Volume" }) { Transform t = page.transform.Find(n); if (t != null) Object.Destroy(t.gameObject); }
            menuPage = page;

            // The main menu button: a copy of Settings opening our page, in the play group right after Load Game. The
            // rows below move down one and the column is tightened back to its old height, so Exit keeps its place and its
            // gap (playtest: squeezed between Voice and Exit, Exit was hard to see).
            GameObject button = Object.Instantiate(settings.gameObject, settings.parent).Cast<GameObject>();
            button.name = "Button MiSideTogether";
            SetText(button, "MULTIPLAYER");
            RectTransform rect = button.GetComponent<RectTransform>(), exitRect = exit.GetComponent<RectTransform>();
            List<RectTransform> order = new List<RectTransform>();
            if (mainLocation.objects != null) foreach (RectTransform r in mainLocation.objects) if (r != null) order.Add(r);
            int at = order.FindIndex(r => r.name.Contains("Load"));
            at = at >= 0 ? at + 1 : Mathf.Min(3, order.Count - 1);
            float top = order[0].anchoredPosition.y, span = top - order[order.Count - 1].anchoredPosition.y;
            float slot = top - order[at - 1].anchoredPosition.y + RowStep, scale = span / (span + RowStep);
            for (int i = 0; i < order.Count; i++)
            {
                float offset = top - order[i].anchoredPosition.y + (i >= at ? RowStep : 0);
                order[i].anchoredPosition = new Vector2(order[i].anchoredPosition.x, top - offset * scale);
            }
            rect.anchoredPosition = new Vector2(order[at - 1].anchoredPosition.x, top - slot * scale);
            button.transform.SetSiblingIndex(order[at].GetSiblingIndex());
            MenuNextLocation next = button.GetComponent<MenuNextLocation>();
            if (next != null) next.nextLocation = page;
            mainLocation.objects.Insert(at, rect);
            // MenuLocation sizes its per-row state in Start(); re-run it so the game knows about the new row.
            RestartLocation(mainLocation);
            Link(order[at - 1].GetComponent<ButtonMouseClick>(), button.GetComponent<ButtonMouseClick>(), order[at].GetComponent<ButtonMouseClick>());
            menuButton = button;
            rowsSignature = "";
            RefreshRows();
            Log("Native menu: MULTIPLAYER added to the main menu");
        }

        // Keyboard / gamepad navigation between neighbouring buttons, as the game wires its own.
        static void Link(ButtonMouseClick above, ButtonMouseClick button, ButtonMouseClick below)
        {
            if (button == null) return;
            button.changeUp = above; button.changeDown = below;
            if (above != null) above.changeDown = button;
            if (below != null) below.changeUp = button;
        }

        static void SetText(GameObject button, string text)
        {
            foreach (Localization_UIText l in button.GetComponentsInChildren<Localization_UIText>(true)) Object.DestroyImmediate(l);
            foreach (Text t in button.GetComponentsInChildren<Text>(true)) t.text = text;
        }

        List<MenuRow> MenuRows()
        {
            List<MenuRow> list = new List<MenuRow>();
            if (!Active)
            {
                list.Add(new MenuRow { Text = "HOST GAME", Click = StartHost });
                foreach (Steam.Friend f in Steam.FriendsInGame())
                {
                    if (string.IsNullOrEmpty(f.Hosting)) continue;
                    string[] p = f.Hosting.Split('|');
                    bool ok = p.Length >= 4 && p[0] == Wire.Version.ToString() && p[1] == ModVersion;
                    ulong id = f.Id; string name = f.Name;
                    list.Add(new MenuRow { Text = "JOIN " + name.ToUpperInvariant() + " (STEAM" + (p.Length >= 4 ? ", " + p[3] + "/4" : "") + ")" + (ok ? "" : " - NEEDS " + (p.Length > 1 ? p[1] : "?")), Click = ok ? (Action)(() => StartJoinSteam(id, name)) : null });
                }
                foreach (LanGame g in VisibleLanGames())
                {
                    LanGame game = g;
                    list.Add(new MenuRow { Text = "JOIN " + g.Name.ToUpperInvariant() + " (LAN, " + g.Players + "/4)", Click = g.Compatible ? (Action)(() => { Address = game.Address; portText = game.Port.ToString(); StartJoin(); }) : null });
                }
                list.Add(new MenuRow { Text = "JOIN BY ADDRESS (F5)", Click = () => SetPanel(true) });
            }
            else
            {
                list.Add(new MenuRow { Text = (Hosting ? "HOSTING" : Connected ? "CONNECTED" : "CONNECTING") + " - " + (1 + AcceptedCount) + " PLAYER" + (AcceptedCount == 0 ? "" : "S"), Click = null });
                foreach (Peer p in peers.Values) if (p.Accepted) list.Add(new MenuRow { Text = "  " + p.Name.ToUpperInvariant() + (p.Id == 0 ? " (HOST)" : ""), Click = null });
                HostTransport ht = Network as HostTransport;
                if (ht != null && ht.Steam) list.Add(new MenuRow { Text = "INVITE A STEAM FRIEND", Click = () => Steam.InviteOverlay("+mt_join " + Steam.Self) });
                list.Add(new MenuRow { Text = Hosting ? "STOP HOSTING" : "LEAVE", Click = () => { leftOnPurpose = true; Shutdown(); Tell("Disconnected."); } });
            }
            return list;
        }

        void RefreshRows()
        {
            if (menuPage == null || rowTemplate == null || pageBack == null) return;
            List<MenuRow> wanted = MenuRows();
            string signature = "";
            foreach (MenuRow r in wanted) signature += r.Text + (r.Click != null ? "*" : "") + "|";
            if (signature == rowsSignature) return;
            rowsSignature = signature;
            foreach (GameObject old in rows) if (old != null) Object.Destroy(old);
            rows.Clear();
            List<RectTransform> objects = new List<RectTransform>();
            Transform title = menuPage.transform.Find("Text");
            if (title != null) objects.Add(title.GetComponent<RectTransform>());
            ButtonMouseClick previous = null;
            for (int i = 0; i < wanted.Count && i < 10; i++)
            {
                MenuRow row = wanted[i];
                GameObject go = Object.Instantiate(rowTemplate, rowTemplate.transform.parent).Cast<GameObject>();
                go.name = "Button MiSideTogether " + i;
                SetText(go, row.Text);
                RectTransform rect = go.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, FirstRow - RowStep * i);
                MenuNextLocation next = go.GetComponent<MenuNextLocation>();
                if (next != null) Object.DestroyImmediate(next);
                ButtonMouseClick click = go.GetComponent<ButtonMouseClick>();
                if (click != null)
                {
                    // Our own action replaces whatever the copied button did.
                    for (int k = 0; k < click.eventClick.GetPersistentEventCount(); k++) click.eventClick.SetPersistentListenerState(k, UnityEventCallState.Off);
                    click.eventClick.RemoveAllListeners();
                    Action action = row.Click;
                    if (action != null) click.eventClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => { try { action(); rowsSignature = ""; nextMenuRefresh = 0; } catch (Exception ex) { Log("Menu action failed: " + ex.Message); } })));
                    else click.interactable = false;
                    Link(previous, click, null);
                    previous = click;
                }
                go.SetActive(true);
                rows.Add(go);
                objects.Add(rect);
            }
            pageBack.anchoredPosition = new Vector2(pageBack.anchoredPosition.x, FirstRow - RowStep * rows.Count - 15);
            ButtonMouseClick backClick = pageBack.GetComponent<ButtonMouseClick>();
            if (previous != null && backClick != null) Link(previous, backClick, null);
            objects.Add(pageBack);
            if (pageLocation != null && pageLocation.objects != null)
            {
                pageLocation.objects.Clear();
                foreach (RectTransform r in objects) pageLocation.objects.Add(r);
                RestartLocation(pageLocation);
            }
        }

        static void RestartLocation(MenuLocation location)
        {
            if (location == null) return;
            try { location.Start(); }
            catch (Exception ex) { Log("Native menu: page re-initialisation failed: " + ex.Message); }
        }
    }
}
