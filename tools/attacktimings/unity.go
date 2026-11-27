package main

import (
	"bytes"
	"encoding/binary"
	"errors"
	"fmt"
	"math"
	"path"
	"strings"
)

// Reader for the parts of Unity 6 asset bundles this tool needs: UnityFS container, SerializedFile, type trees.
const (
	classAnimationClip      = 74
	classAnimatorController = 91
	alignBytes              = 0x4000
)

type serializedObject struct {
	pathID int64
	start  int64
	class  int32
	tree   *typeNode
}

type serializedFile struct {
	name      string
	data      []byte
	externals []string
	objects   []serializedObject
}

// bundleFiles returns the serialized files of one UnityFS bundle.
func bundleFiles(b []byte) ([]*serializedFile, error) {
	r := &reader{b: b, order: binary.BigEndian}
	if r.cstring() != "UnityFS" {
		return nil, errors.New("not a UnityFS bundle")
	}
	format := r.u32()
	r.cstring()
	r.cstring()
	r.i64()
	infoSize, infoFull, flags := int(r.u32()), int(r.u32()), r.u32()
	if format >= 7 {
		r.align(16)
	}
	var packed []byte
	if flags&0x80 != 0 {
		packed = b[len(b)-infoSize:]
	} else {
		packed = r.bytes(infoSize)
	}
	info, err := decompress(packed, infoFull, flags&0x3f)
	if err != nil {
		return nil, err
	}
	ir := &reader{b: info, order: binary.BigEndian}
	ir.bytes(16)
	type block struct {
		full, packed int
		flags        uint32
	}
	blocks := make([]block, ir.i32())
	for i := range blocks {
		blocks[i] = block{int(ir.u32()), int(ir.u32()), uint32(ir.u16())}
	}
	type dirNode struct {
		off, size int64
		flags     uint32
		name      string
	}
	nodes := make([]dirNode, ir.i32())
	var need int64
	for i := range nodes {
		nodes[i] = dirNode{ir.i64(), ir.i64(), ir.u32(), ir.cstring()}
		if nodes[i].flags&4 != 0 {
			need = max(need, nodes[i].off+nodes[i].size)
		}
	}
	if flags&0x200 != 0 {
		r.align(16)
	}
	data := make([]byte, 0, need)
	for _, bl := range blocks {
		if int64(len(data)) >= need { // resource payloads behind the serialized files are never read
			break
		}
		part, err := decompress(r.bytes(bl.packed), bl.full, bl.flags&0x3f)
		if err != nil {
			return nil, err
		}
		data = append(data, part...)
	}
	var out []*serializedFile
	for _, n := range nodes {
		if n.flags&4 == 0 {
			continue
		}
		f, err := parseSerialized(n.name, data[n.off:n.off+n.size])
		if err != nil {
			return nil, fmt.Errorf("%s: %w", n.name, err)
		}
		out = append(out, f)
	}
	return out, nil
}

func decompress(b []byte, size int, kind uint32) ([]byte, error) {
	switch kind {
	case 0:
		return b, nil
	case 1:
		return lzmaBlock(b, size)
	case 2, 3:
		return lz4Block(b, size)
	}
	return nil, fmt.Errorf("compression %d", kind)
}

func parseSerialized(name string, b []byte) (f *serializedFile, err error) {
	defer func() {
		if e := recover(); e != nil {
			f, err = nil, fmt.Errorf("serialized file: %v", e)
		}
	}()
	r := &reader{b: b, order: binary.BigEndian}
	r.u32()
	r.u32()
	version := r.u32()
	dataOffset := int64(r.u32())
	if version < 21 {
		return nil, fmt.Errorf("serialized version %d", version)
	}
	bigEndian := r.u8() != 0
	r.bytes(3)
	if version >= 22 {
		r.u32()
		r.i64()
		dataOffset = r.i64()
		r.i64()
	}
	if bigEndian {
		return nil, errors.New("big endian metadata")
	}
	r.order = binary.LittleEndian
	r.cstring()
	r.i32()
	withTrees := r.u8() != 0
	if !withTrees {
		return nil, errors.New("no type trees")
	}
	type serializedType struct {
		class int32
		nodes []byte
		strs  []byte
		count int
	}
	types := make([]serializedType, r.i32())
	for i := range types {
		t := &types[i]
		t.class = r.i32()
		r.u8()
		r.u16()
		if t.class == 114 {
			r.bytes(16)
		}
		r.bytes(16)
		t.count = int(r.i32())
		strSize := int(r.i32())
		t.nodes = r.bytes(t.count * 32)
		t.strs = r.bytes(strSize)
		for n := r.i32(); n > 0; n-- {
			r.i32()
		}
	}
	trees := map[int]*typeNode{}
	f = &serializedFile{name: name, data: b}
	for n := int(r.i32()); n > 0; n-- {
		r.align(4)
		id := r.i64()
		var start int64
		if version >= 22 {
			start = r.i64()
		} else {
			start = int64(r.u32())
		}
		r.u32()
		idx := int(r.i32())
		t := &types[idx]
		if t.class != classAnimationClip && t.class != classAnimatorController {
			continue
		}
		if trees[idx] == nil {
			trees[idx] = parseTree(t.nodes, t.strs, t.count)
		}
		f.objects = append(f.objects, serializedObject{id, dataOffset + start, t.class, trees[idx]})
	}
	for n := r.i32(); n > 0; n-- {
		r.i32()
		r.align(4)
		r.i64()
	}
	for n := r.i32(); n > 0; n-- {
		r.cstring()
		r.bytes(20)
		f.externals = append(f.externals, r.cstring())
	}
	return f, nil
}

type typeNode struct {
	typ, name string
	aligned   bool
	prim      int // fixed primitive size, 0 = none
	kids      []*typeNode
}

var primSizes = map[string]int{
	"SInt8": 1, "UInt8": 1, "char": 1, "bool": 1, "short": 2, "SInt16": 2, "unsigned short": 2, "UInt16": 2,
	"int": 4, "SInt32": 4, "unsigned int": 4, "UInt32": 4, "Type*": 4, "float": 4,
	"long long": 8, "SInt64": 8, "unsigned long long": 8, "UInt64": 8, "FileSize": 8, "double": 8,
}

func parseTree(raw, strs []byte, count int) *typeNode {
	str := func(v uint32) string {
		src := strs
		if v&0x80000000 != 0 {
			src = []byte(commonStrings)
			v &= 0x7fffffff
		}
		s := src[v:]
		return string(s[:bytes.IndexByte(s, 0)])
	}
	var root *typeNode
	var parents []*typeNode
	for i := range count {
		e := raw[i*32:]
		level := int(e[2])
		n := &typeNode{
			typ:     str(binary.LittleEndian.Uint32(e[4:])),
			name:    str(binary.LittleEndian.Uint32(e[8:])),
			aligned: binary.LittleEndian.Uint32(e[20:])&alignBytes != 0,
		}
		n.prim = primSizes[n.typ]
		parents = append(parents[:level], n)
		if level == 0 {
			root = n
		} else {
			p := parents[level-1]
			p.kids = append(p.kids, n)
		}
	}
	return root
}

// readObject decodes an object by its type tree (same rules as UnityPy's read_typetree). Arrays of primitives are
// skipped (nil): nothing here reads them, and clips are mostly such arrays.
func readObject(f *serializedFile, o serializedObject) (v map[string]any, err error) {
	defer func() {
		if e := recover(); e != nil {
			v, err = nil, fmt.Errorf("%v", e)
		}
	}()
	r := &reader{b: f.data, pos: int(o.start), order: binary.LittleEndian}
	m, ok := r.value(o.tree).(map[string]any)
	if !ok {
		return nil, fmt.Errorf("object %d is not a map", o.pathID)
	}
	return m, nil
}

func (r *reader) primitive(n *typeNode) any {
	switch n.typ {
	case "SInt8":
		b := int64(r.u8())
		if b >= 0x80 {
			b -= 0x100
		}
		return b
	case "UInt8", "char":
		return int64(r.u8())
	case "bool":
		return r.u8() != 0
	case "short", "SInt16":
		v := int64(r.u16())
		if v >= 0x8000 {
			v -= 0x10000
		}
		return v
	case "unsigned short", "UInt16":
		return int64(r.u16())
	case "int", "SInt32":
		return int64(r.i32())
	case "unsigned int", "UInt32", "Type*":
		return int64(r.u32())
	case "float":
		return float64(math.Float32frombits(r.u32()))
	case "double":
		return math.Float64frombits(r.u64())
	}
	return r.i64()
}

func (r *reader) alignedString() string {
	n := int(r.i32())
	if n <= 0 || n > len(r.b)-r.pos {
		return ""
	}
	s := string(r.bytes(n))
	r.align(4)
	return s
}

func (r *reader) class(n *typeNode) map[string]any {
	m := make(map[string]any, len(n.kids))
	for _, k := range n.kids {
		m[k.name] = r.value(k)
	}
	return m
}

func isVector(n *typeNode) bool { return len(n.kids) > 0 && n.kids[0].typ == "Array" }

func (r *reader) elements(sub *typeNode, size int) any {
	if sub.prim > 0 {
		r.pos += size * sub.prim
		return nil
	}
	out := make([]any, size)
	for i := range out {
		out[i] = r.value(sub)
	}
	return out
}

func (r *reader) pairPart(n *typeNode) any {
	switch {
	case n.prim > 0:
		return r.primitive(n)
	case n.typ == "string":
		return r.alignedString()
	}
	return r.value(n)
}

// valueArray = UnityPy read_value_array: an element type with the align flag aligns once, after the whole array.
func (r *reader) valueArray(n *typeNode, size int) any {
	align := n.aligned
	var v any
	switch {
	case n.prim > 0:
		r.pos += size * n.prim
	case n.typ == "string":
		out := make([]any, size)
		for i := range out {
			out[i] = r.alignedString()
		}
		v = out
	case n.typ == "TypelessData":
		for range size {
			r.bytes(int(r.i32()))
		}
	case n.typ == "pair":
		out := make([]any, size)
		for i := range out {
			first := r.pairPart(n.kids[0])
			out[i] = map[string]any{"first": first, "second": r.pairPart(n.kids[1])}
		}
		v = out
	case isVector(n):
		align = align || n.kids[0].aligned
		sub := n.kids[0].kids[1]
		out := make([]any, size)
		for i := range out {
			count := int(r.i32())
			if sub.aligned {
				out[i] = r.valueArray(sub, count)
			} else {
				out[i] = r.elements(sub, count)
			}
		}
		v = out
	default:
		out := make([]any, size)
		for i := range out {
			out[i] = r.class(n)
		}
		v = out
	}
	if align {
		r.align(4)
	}
	return v
}

func (r *reader) value(n *typeNode) any {
	align := n.aligned
	var v any
	switch {
	case n.prim > 0:
		v = r.primitive(n)
	case n.typ == "string":
		v = r.alignedString()
	case n.typ == "TypelessData":
		r.bytes(int(r.i32()))
	case n.typ == "pair":
		first := r.value(n.kids[0])
		v = map[string]any{"first": first, "second": r.value(n.kids[1])}
	case isVector(n):
		align = align || n.kids[0].aligned
		size := int(r.i32())
		if size < 0 {
			panic("negative array size")
		}
		sub := n.kids[0].kids[1]
		if sub.aligned {
			v = r.valueArray(sub, size)
		} else {
			v = r.elements(sub, size)
		}
	default:
		v = r.class(n)
	}
	if align {
		r.align(4)
	}
	return v
}

type reader struct {
	b     []byte
	pos   int
	order binary.ByteOrder
}

func (r *reader) bytes(n int) []byte {
	s := r.b[r.pos : r.pos+n]
	r.pos += n
	return s
}
func (r *reader) u8() byte    { return r.bytes(1)[0] }
func (r *reader) u16() uint16 { return r.order.Uint16(r.bytes(2)) }
func (r *reader) u32() uint32 { return r.order.Uint32(r.bytes(4)) }
func (r *reader) u64() uint64 { return r.order.Uint64(r.bytes(8)) }
func (r *reader) i32() int32 {
	u := r.u32()
	if u <= math.MaxInt32 {
		return int32(u)
	}
	v := ^u
	if v <= math.MaxInt32 {
		return -int32(v) - 1
	}
	return 0
}
func (r *reader) i64() int64 {
	u := r.u64()
	if u <= math.MaxInt64 {
		return int64(u)
	}
	v := ^u
	if v <= math.MaxInt64 {
		return -int64(v) - 1
	}
	return 0
}
func (r *reader) align(n int) { r.pos = (r.pos + n - 1) / n * n }
func (r *reader) cstring() string {
	end := bytes.IndexByte(r.b[r.pos:], 0)
	s := string(r.b[r.pos : r.pos+end])
	r.pos += end + 1
	return s
}

func cabName(name string) string {
	return strings.ToLower(path.Base(strings.ReplaceAll(name, "\\", "/")))
}
