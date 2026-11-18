// attacktimings bakes Morgott.UI WeaponAttackRate's tables from the game's Player_animator and animation clips.
//
//	go run .          compares the tables in WeaponAttackRate.cs with the game files (exit 1 on a difference)
//	go run . -write   rewrites them
package main

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"slices"
	"sort"
	"strconv"
	"strings"
)

const noClip = 0xFFFFFFFF

// Plugins whose embedded bundles hold player attack clips (BalanceSim extract_assets.PLUGIN_DLLS).
var pluginDLLs = []string{
	`BepInEx\plugins\Warfare\Warfare.dll`, `BepInEx\plugins\Armory\Armory.dll`, `BepInEx\plugins\Wizardry\Wizardry.dll`,
	`BepInEx\plugins\AeehyehssReeper-ChaosArmor\ChaosArmor.dll`, `BepInEx\plugins\BoneAppetit\BoneAppetit.dll`, `BepInEx\plugins\HoneyPlus.dll`,
}

// Warfare swaps the clips named Attack1..3 of the player controller and plays swing_longsword<j> for these triggers
// (KG_Managers.AnimationReplaceManager, decompiled Warfare.dll: WarfarePlugin.cs:411-436).
var runtimeSets = [][]string{
	{"OverheadSlashBattleaxe"}, {"ChargedSlashTwoHanded"}, {"SpinAttackNonSpear"},
	{"DualBladeMain0", "DualBladeMain1", "DualBladeMain2"}, {"CultistStaffMain0", "CultistStaffMain1", "CultistStaffMain2"},
	{"SkrymirSecondary_TW"}, {"StaffStormSecondary_TW"}, {"DualAxeMain0", "DualAxeMain1", "DualAxeMain2"},
	{"DualAxeSecondary_TW"}, {"DualSwordsMain0", "DualSwordsMain1", "DualSwordsMain2"}, {"DualSwordsSecondary_TW"},
	{"DualHammerMain0", "DualHammerMain1", "DualHammerMain2"}, {"DualHammerSecondary_TW"},
	{"DualSpearMain0", "DualSpearMain1", "DualSpearMain2"}, {"DualSpearSecondary_TW"}, {"JumpSecondaryAttack_TW"},
	{"WarpikeMain0", "WarpikeMain1", "WarpikeMain2"}, {"WarpikeSecondary_TW"}, {"FistSlamJumpAnimation_TW"},
	{"FistLeftRightComboAnimation_TW"}, {"FistAttackSecondary_TW"}, {"FistDoubleHit_TW"}, {"RightPowerUppercut_TW"},
	{"ThrowAttackMain0", "ThrowAttackMain1", "ThrowAttackMain2"}, {"ThrowAttackSecondary_TW"},
	{"YagluthBlade_Secondary_TW"},
}

var keptEvents = map[string]bool{"Hit": true, "OnAttackTrigger": true, "Chain": true, "ResetChain": true, "Stop": true, "Speed": true, "TrailOn": true, "TrailOff": true}

type ref struct {
	cab string
	id  int64
}

type clipEvent struct {
	t, f float64
	fn   string
}

type clip struct {
	name   string
	length float64
	events []clipEvent
	source string
}

type rawController struct {
	name  string
	tt    map[string]any
	tos   []any
	clips []*ref
}

type assets struct {
	ctrls map[ref]*rawController
	order []ref
	clips map[ref]*clip
}

func field(m any, k string) any { return m.(map[string]any)[k] }
func list(m any, k string) []any {
	v, _ := field(m, k).([]any)
	return v
}
func data(m any) any { return field(m, "data") }
func num(m any, k string) float64 {
	switch v := field(m, k).(type) {
	case float64:
		return v
	case int64:
		return float64(v)
	}
	panic("not a number: " + k)
}
func integer(m any, k string) int64 { return field(m, k).(int64) }
func truthy(m any, k string) bool {
	switch v := field(m, k).(type) {
	case bool:
		return v
	case int64:
		return v != 0
	}
	return false
}

// round = Python round(v, n): nearest decimal of the exact binary value.
func round(v float64, n int) float64 {
	r, _ := strconv.ParseFloat(strconv.FormatFloat(v, 'f', n, 64), 64)
	return r
}

func pptr(v any, f *serializedFile) *ref {
	id := integer(v, "m_PathID")
	if id == 0 {
		return nil
	}
	file := integer(v, "m_FileID")
	if file == 0 {
		return &ref{cabName(f.name), id}
	}
	if int(file) > len(f.externals) {
		return nil
	}
	return &ref{cabName(f.externals[file-1]), id}
}

func (a *assets) load(label string, b []byte) {
	files, err := bundleFiles(b)
	if err != nil {
		fmt.Fprintln(os.Stderr, "load fail", label, err)
		return
	}
	for _, f := range files {
		cab := cabName(f.name)
		for _, o := range f.objects {
			tt, err := readObject(f, o)
			if err != nil {
				fmt.Fprintln(os.Stderr, "read fail", cab, o.pathID, err)
				continue
			}
			key := ref{cab, o.pathID}
			if o.class == classAnimatorController {
				c := &rawController{name: field(tt, "m_Name").(string), tt: field(tt, "m_Controller").(map[string]any), tos: list(tt, "m_TOS")}
				for _, p := range list(tt, "m_AnimationClips") {
					c.clips = append(c.clips, pptr(p, f))
				}
				if a.ctrls[key] == nil {
					a.order = append(a.order, key)
				}
				a.ctrls[key] = c
				continue
			}
			mc := field(tt, "m_MuscleClip")
			c := &clip{name: field(tt, "m_Name").(string), length: num(mc, "m_StopTime") - num(mc, "m_StartTime"), source: label}
			for _, e := range list(tt, "m_Events") {
				c.events = append(c.events, clipEvent{round(num(e, "time"), 5), num(e, "floatParameter"), field(e, "functionName").(string)})
			}
			a.clips[key] = c
		}
	}
}

// embeddedBundles finds UnityFS bundles stored as resources of a plugin DLL.
func embeddedBundles(b []byte) [][]byte {
	sig := []byte("UnityFS\x00")
	var out [][]byte
	for off := 0; ; {
		i := bytes.Index(b[off:], sig)
		if i < 0 {
			return out
		}
		off += i
		r := &reader{b: b, pos: off + len(sig) + 4, order: binary.BigEndian}
		r.cstring()
		r.cstring()
		if size := r.i64(); size > 0 && size <= int64(len(b)-off) {
			out = append(out, b[off:off+int(size)])
			off += int(size)
		} else {
			off++
		}
	}
}

func (a *assets) clipByName(name, dll string) *ref {
	var hits []ref
	for k, c := range a.clips {
		if c.name == name && strings.HasPrefix(c.source, dll+"@") {
			hits = append(hits, k)
		}
	}
	if len(hits) == 0 {
		return nil
	}
	slices.SortFunc(hits, func(x, y ref) int {
		if c := strings.Compare(x.cab, y.cab); c != 0 {
			return c
		}
		return int(min(max(x.id-y.id, -1), 1))
	})
	return &hits[0]
}

type controller struct {
	a         *assets
	tos       map[int64]string
	hash      map[string]int64
	clips     []*ref
	sms       []any
	layers    []int64
	attackTag int64
}

func newController(a *assets, c *rawController, remap map[string]*ref) *controller {
	x := &controller{a: a, tos: map[int64]string{}, hash: map[string]int64{}}
	for _, e := range c.tos {
		k, v := integer(e, "first"), field(e, "second").(string)
		x.tos[k] = v
		x.hash[v] = k
	}
	for _, r := range c.clips {
		if r != nil {
			if cl := a.clips[*r]; cl != nil && remap[cl.name] != nil {
				r = remap[cl.name]
			}
		}
		x.clips = append(x.clips, r)
	}
	x.attackTag = x.hash["attack"]
	for _, s := range list(c.tt, "m_StateMachineArray") {
		x.sms = append(x.sms, data(s))
	}
	for _, l := range list(c.tt, "m_LayerArray") {
		x.layers = append(x.layers, integer(data(l), "m_StateMachineIndex"))
	}
	return x
}

type transition struct {
	layer    int
	sm       any
	anyState bool
	d        any
}

// transitions lists those with a condition on event `name` in the given mode (1 = If, 2 = IfNot).
func (c *controller) transitions(name string, mode int64) []transition {
	h, ok := c.hash[name]
	if !ok {
		return nil
	}
	var out []transition
	for li, smi := range c.layers {
		sm := c.sms[smi]
		add := func(trs []any, anyState bool) {
			for _, tr := range trs {
				d := data(tr)
				for _, cc := range list(d, "m_ConditionConstantArray") {
					cd := data(cc)
					if integer(cd, "m_EventID") == h && integer(cd, "m_ConditionMode") == mode {
						out = append(out, transition{li, sm, anyState, d})
						break
					}
				}
			}
		}
		add(list(sm, "m_AnyStateTransitionConstantArray"), true)
		for _, st := range list(sm, "m_StateConstantArray") {
			add(list(data(st), "m_TransitionConstantArray"), false)
		}
	}
	return out
}

// stateClip is the state's only clip; nil for none, several or an unresolved one.
func (c *controller) stateClip(st any) *clip {
	var found []*clip
	for _, bt := range list(st, "m_BlendTreeConstantArray") {
		for _, n := range list(data(bt), "m_NodeArray") {
			id := integer(data(n), "m_ClipID")
			if id == noClip || int(id) >= len(c.clips) {
				continue
			}
			var cl *clip
			if r := c.clips[id]; r != nil {
				cl = c.a.clips[*r]
			}
			found = append(found, cl)
		}
	}
	if len(found) != 1 {
		return nil
	}
	return found[0]
}

func exitBlend(ex any, length, speed float64) float64 {
	if truthy(ex, "m_HasFixedDuration") {
		return num(ex, "m_TransitionDuration")
	}
	return num(ex, "m_TransitionDuration") * length / speed
}

type timing struct {
	total, blend float64 // attack-tagged span from the trigger; exit blend when nothing may interrupt it, else 0
	chain        float64 // first Chain event inside the span, 0 = none
	chainSpeed   float64 // Animator.speed at that event: the next chain level starts with it
}

func (t timing) full() float64 { return round(t.total+t.blend, 3) }

// simulate walks the attack-tagged states from a trigger: state speed, clip Speed events and exit-time transitions
// (BalanceSim anim_events.py Controller.simulate). start = Animator.speed at the trigger: CharacterAnimEvent resets it
// only outside attacks (:164-167), so a chained level inherits the previous clip's. ok = false for anything it cannot
// state exactly.
func (c *controller) simulate(tr transition, start float64) (res timing, ok bool) {
	states := list(tr.sm, "m_StateConstantArray")
	dest, offset := int(integer(tr.d, "m_DestinationState")), num(tr.d, "m_TransitionOffset")
	aSpeed, t := start, 0.0
	var chains, chainSpeeds []float64
	var blend float64
	var interruption int64 = -1
	finish := func(end float64) (timing, bool) {
		res.total = round(end, 4)
		if interruption == 0 {
			res.blend = blend
		}
		for i, ch := range chains {
			if ch < res.total {
				res.chain, res.chainSpeed = round(ch, 3), chainSpeeds[i]
				break
			}
		}
		return res, true
	}
	for step := 0; step < 6; step++ {
		if dest >= len(states) {
			return res, false
		}
		st := data(states[dest])
		if integer(st, "m_TagID") != c.attackTag {
			if step == 0 {
				return res, false
			}
			return finish(t)
		}
		cl := c.stateClip(st)
		if cl == nil || integer(st, "m_SpeedParamID") != 0 {
			return res, false
		}
		var ex any
		for _, x := range list(st, "m_TransitionConstantArray") {
			d := data(x)
			if truthy(d, "m_HasExitTime") && len(list(d, "m_ConditionConstantArray")) == 0 {
				ex = d
				break
			}
		}
		if ex == nil {
			return res, false
		}
		length, speed := cl.length, num(st, "m_Speed")
		pos, horizon := offset*length, num(ex, "m_ExitTime")*length
		dAnim := exitBlend(ex, length, speed)
		exitAt, exited := 0.0, false
		events := slices.Clone(cl.events)
		sort.SliceStable(events, func(i, j int) bool { return events[i].t < events[j].t })
		for _, e := range events {
			if !keptEvents[e.fn] || e.t < pos-1e-6 {
				continue
			}
			if !exited && e.t > horizon+1e-6 {
				exitAt, exited = t+(horizon-pos)/(speed*aSpeed), true
				t, pos = exitAt, horizon
			}
			at := t + (e.t-pos)/(speed*aSpeed)
			if exited && at > exitAt+dAnim/aSpeed+1e-6 {
				break
			}
			t, pos = at, e.t
			switch e.fn {
			case "Chain":
				chains, chainSpeeds = append(chains, round(t, 4)), append(chainSpeeds, aSpeed)
			case "Speed":
				if e.f > 0 {
					aSpeed = e.f
				}
			}
		}
		if !exited {
			exitAt = t + (horizon-pos)/(speed*aSpeed)
		}
		blend, interruption = round(dAnim/aSpeed, 4), integer(ex, "m_InterruptionSource")
		next := int(integer(ex, "m_DestinationState"))
		if next < len(states) && integer(data(states[next]), "m_TagID") == c.attackTag {
			t, dest, offset = exitAt, next, num(ex, "m_TransitionOffset")
			continue
		}
		return finish(exitAt)
	}
	return res, false
}

func (c *controller) attack(trigger string, start float64) (timing, bool) {
	trs := c.transitions(trigger, 1)
	if len(trs) == 0 {
		return timing{}, false
	}
	sort.SliceStable(trs, func(i, j int) bool {
		if trs[i].anyState != trs[j].anyState {
			return trs[i].anyState
		}
		return trs[i].layer < trs[j].layer
	})
	return c.simulate(trs[0], start)
}

// reloadTail = seconds the minor-action tag outlives the reload timer: the "<anim>_done" state with its exit blend, or
// the blend out of the reload state when the animator has no done state.
func (c *controller) reloadTail(anim string) (float64, bool) {
	if trs := c.transitions(anim+"_done", 1); len(trs) > 0 {
		states := list(trs[0].sm, "m_StateConstantArray")
		st := data(states[integer(trs[0].d, "m_DestinationState")])
		cl := c.stateClip(st)
		if cl == nil {
			return 0, false
		}
		speed := num(st, "m_Speed")
		for _, x := range list(st, "m_TransitionConstantArray") {
			if d := data(x); truthy(d, "m_HasExitTime") {
				return num(d, "m_ExitTime")*cl.length/speed + exitBlend(d, cl.length, speed), true
			}
		}
		return 0, false
	}
	if trs := c.transitions(anim, 2); len(trs) > 0 {
		return num(trs[0].d, "m_TransitionDuration"), true
	}
	return 0, false
}

type attackData struct {
	Animation  string `json:"m_attackAnimation"`
	Chain      int    `json:"m_attackChainLevels"`
	Random     int    `json:"m_attackRandomAnimations"`
	Reload     bool   `json:"m_requiresReload"`
	ReloadAnim string `json:"m_reloadAnimation"`
}

func triggers(a attackData) []string {
	n := 0
	if a.Chain > 1 {
		n = a.Chain
	} else if a.Random >= 2 {
		n = a.Random
	}
	if n == 0 {
		return []string{a.Animation}
	}
	out := make([]string, n)
	for i := range out {
		out[i] = a.Animation + strconv.Itoa(i)
	}
	return out
}

func number(v float64) string { return strconv.FormatFloat(v, 'f', -1, 64) }

func bake(game, dumpPath string) (triggerLines, reloadLines []string, err error) {
	a := &assets{ctrls: map[ref]*rawController{}, clips: map[ref]*clip{}}
	root := filepath.Join(game, `valheim_Data\StreamingAssets\SoftRef\Bundles`)
	entries, err := os.ReadDir(root)
	if err != nil {
		return nil, nil, err
	}
	for _, e := range entries {
		if b, err := os.ReadFile(filepath.Join(root, e.Name())); err == nil {
			a.load(e.Name(), b)
		}
	}
	for _, rel := range pluginDLLs {
		b, err := os.ReadFile(filepath.Join(game, rel))
		if err != nil {
			continue
		}
		for i, bundle := range embeddedBundles(b) {
			a.load(fmt.Sprintf("%s@%d", filepath.Base(rel), i), bundle)
		}
	}
	var base *rawController
	for _, k := range a.order {
		if a.ctrls[k].name == "Player_animator" {
			base = a.ctrls[k]
			break
		}
	}
	if base == nil {
		return nil, nil, fmt.Errorf("no Player_animator under %s", root)
	}
	player := newController(a, base, nil)
	type swap struct {
		c       *controller
		trigger string
	}
	swaps := map[string]swap{}
	for _, set := range runtimeSets {
		remap := map[string]*ref{}
		for i, name := range set {
			r := a.clipByName(name, "Warfare.dll")
			if r == nil {
				remap = nil
				break
			}
			remap["Attack"+strconv.Itoa(i+1)] = r
		}
		if remap == nil {
			continue
		}
		c := newController(a, base, remap)
		for j, name := range set {
			swaps[name] = swap{c, "swing_longsword" + strconv.Itoa(j)}
		}
	}
	raw, err := os.ReadFile(dumpPath)
	if err != nil {
		return nil, nil, err
	}
	var dump struct {
		Items []struct {
			Shared struct {
				Attack attackData `json:"m_attack"`
			} `json:"shared"`
		} `json:"items"`
	}
	if err := json.Unmarshal(raw, &dump); err != nil {
		return nil, nil, err
	}
	found := map[string]timing{}
	seen := map[string]bool{}
	fireOf := map[string]string{}
	for _, it := range dump.Items {
		at := it.Shared.Attack
		if at.Animation == "" {
			continue
		}
		names := triggers(at)
		speed := 1.0
		for i, t := range names {
			if !seen[t] {
				seen[t] = true
				c, name := player, t
				if s, ok := swaps[t]; ok {
					c, name = s.c, s.trigger
				}
				if v, ok := c.attack(name, speed); ok {
					found[t] = v
				}
			}
			speed = 1
			if v := found[t]; at.Chain > 1 && i < len(names)-1 && v.chain > 0 {
				speed = v.chainSpeed
			}
		}
		if at.Reload && at.ReloadAnim != "" && fireOf[at.ReloadAnim] == "" {
			fireOf[at.ReloadAnim] = names[0]
		}
	}
	for t, v := range found {
		triggerLines = append(triggerLines, fmt.Sprintf(`            ["%s"] = new Timing(%sf, %sf),`, t, number(v.full()), number(v.chain)))
	}
	for anim, fire := range fireOf {
		tail, ok := player.reloadTail(anim)
		if !ok {
			return nil, nil, fmt.Errorf("reload %s: no way out of its state", anim)
		}
		// The table counts from the fire animation's Full, which already holds its exit blend.
		reloadLines = append(reloadLines, fmt.Sprintf(`            ["%s"] = %sf,`, anim, number(round(tail-found[fire].blend, 3))))
	}
	sort.Slice(triggerLines, func(i, j int) bool { return tableKey(triggerLines[i]) < tableKey(triggerLines[j]) })
	sort.Strings(reloadLines)
	return triggerLines, reloadLines, nil
}

func tableKey(line string) string { return line[:strings.Index(line, "]")] }

// replaceBody swaps the initializer lines of the dictionary declared on the line containing marker.
func replaceBody(src, marker string, lines []string) (string, error) {
	i := strings.Index(src, marker)
	if i < 0 {
		return "", fmt.Errorf("%q not found", marker)
	}
	const open, closing = "        {\n", "        };\n"
	start := strings.Index(src[i:], open)
	if start < 0 {
		return "", fmt.Errorf("%q: no body", marker)
	}
	start += i + len(open)
	end := strings.Index(src[start:], closing)
	if end < 0 {
		return "", fmt.Errorf("%q: no end", marker)
	}
	return src[:start] + strings.Join(lines, "\n") + "\n" + src[start+end:], nil
}

func main() {
	game := flag.String("game", `D:\Steam\steamapps\common\Valheim`, "game folder (with the pack's plugins)")
	dump := flag.String("dump", `E:\DEV\Valheim\BalanceSim\data\dump-stats.json`, "95-statdump output: the attacks of all items")
	cs := flag.String("cs", `..\..\src\Morgott.UI\Screens\WeaponAttackRate.cs`, "file holding the tables")
	write := flag.Bool("write", false, "rewrite the tables instead of comparing")
	flag.Parse()
	if err := run(*game, *dump, *cs, *write); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run(game, dump, cs string, write bool) error {
	triggerLines, reloadLines, err := bake(game, dump)
	if err != nil {
		return err
	}
	raw, err := os.ReadFile(cs)
	if err != nil {
		return err
	}
	src := string(raw)
	out, err := replaceBody(src, "Dictionary<string, Timing> Triggers", triggerLines)
	if err != nil {
		return err
	}
	if out, err = replaceBody(out, "Dictionary<string, float> ReloadTail", reloadLines); err != nil {
		return err
	}
	fmt.Printf("%d triggers, %d reloads\n", len(triggerLines), len(reloadLines))
	if out == src {
		fmt.Println("tables match")
		return nil
	}
	if write {
		fmt.Println("tables rewritten")
		return os.WriteFile(cs, []byte(out), 0o644)
	}
	return fmt.Errorf("tables differ from the game files (rerun with -write)")
}
