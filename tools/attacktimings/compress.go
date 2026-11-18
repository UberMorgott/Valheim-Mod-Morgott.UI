package main

import "errors"

var errCorrupt = errors.New("corrupt compressed block")

// lz4Block decodes one raw LZ4 block into exactly size bytes.
func lz4Block(src []byte, size int) ([]byte, error) {
	dst := make([]byte, 0, size)
	for p := 0; p < len(src); {
		token := src[p]
		p++
		n := int(token >> 4)
		if n == 15 {
			for {
				if p >= len(src) {
					return nil, errCorrupt
				}
				b := src[p]
				p++
				n += int(b)
				if b != 255 {
					break
				}
			}
		}
		if p+n > len(src) {
			return nil, errCorrupt
		}
		dst = append(dst, src[p:p+n]...)
		p += n
		if p == len(src) {
			break
		}
		if p+2 > len(src) {
			return nil, errCorrupt
		}
		offset := int(src[p]) | int(src[p+1])<<8
		p += 2
		m := int(token & 15)
		if m == 15 {
			for {
				if p >= len(src) {
					return nil, errCorrupt
				}
				b := src[p]
				p++
				m += int(b)
				if b != 255 {
					break
				}
			}
		}
		m += 4
		from := len(dst) - offset
		if offset == 0 || from < 0 {
			return nil, errCorrupt
		}
		for i := 0; i < m; i++ { // the match may overlap its own output
			dst = append(dst, dst[from+i])
		}
	}
	if len(dst) != size {
		return nil, errCorrupt
	}
	return dst, nil
}

// LZMA1 (LZMA specification, LzmaSpec.cpp). Unity's stream: 5 property bytes, then raw range coded data, no size field.
type rangeDecoder struct {
	src       []byte
	pos       int
	rng, code uint32
}

func (r *rangeDecoder) next() uint32 {
	if r.pos >= len(r.src) {
		panic(errCorrupt)
	}
	b := r.src[r.pos]
	r.pos++
	return uint32(b)
}

func (r *rangeDecoder) normalize() {
	if r.rng < 1<<24 {
		r.rng <<= 8
		r.code = r.code<<8 | r.next()
	}
}

func (r *rangeDecoder) bit(p *uint16) uint32 {
	bound := (r.rng >> 11) * uint32(*p)
	var b uint32
	if r.code < bound {
		r.rng = bound
		*p += (2048 - *p) >> 5
	} else {
		r.rng -= bound
		r.code -= bound
		*p -= *p >> 5
		b = 1
	}
	r.normalize()
	return b
}

func (r *rangeDecoder) tree(probs []uint16, bits int) uint32 {
	m := uint32(1)
	for i := 0; i < bits; i++ {
		m = m<<1 + r.bit(&probs[m])
	}
	return m - 1<<bits
}

func (r *rangeDecoder) reverse(probs []uint16, bits int) uint32 {
	m, sym := uint32(1), uint32(0)
	for i := 0; i < bits; i++ {
		b := r.bit(&probs[m])
		m = m<<1 + b
		sym |= b << i
	}
	return sym
}

func (r *rangeDecoder) direct(bits int) uint32 {
	var res uint32
	for ; bits > 0; bits-- {
		r.rng >>= 1
		r.code -= r.rng
		t := 0 - (r.code >> 31)
		r.code += r.rng & t
		r.normalize()
		res = res<<1 + t + 1
	}
	return res
}

type lenDecoder struct {
	choice, choice2 uint16
	low, mid        [16][8]uint16
	high            [256]uint16
}

func (l *lenDecoder) init() {
	l.choice, l.choice2 = 1024, 1024
	for i := range l.low {
		fill(l.low[i][:])
		fill(l.mid[i][:])
	}
	fill(l.high[:])
}

func (l *lenDecoder) decode(r *rangeDecoder, posState uint32) uint32 {
	if r.bit(&l.choice) == 0 {
		return r.tree(l.low[posState][:], 3)
	}
	if r.bit(&l.choice2) == 0 {
		return 8 + r.tree(l.mid[posState][:], 3)
	}
	return 16 + r.tree(l.high[:], 8)
}

func fill(p []uint16) {
	for i := range p {
		p[i] = 1024
	}
}

func lzmaBlock(src []byte, size int) (out []byte, err error) {
	defer func() {
		if e := recover(); e != nil {
			out, err = nil, errCorrupt
		}
	}()
	if len(src) < 5 || src[0] >= 225 {
		return nil, errCorrupt
	}
	d := uint32(src[0])
	lc := d % 9
	d /= 9
	pb := d / 5
	lp := d % 5
	r := &rangeDecoder{src: src, pos: 5, rng: 0xFFFFFFFF}
	if r.next() != 0 {
		return nil, errCorrupt
	}
	for i := 0; i < 4; i++ {
		r.code = r.code<<8 | r.next()
	}
	lit := make([]uint16, 0x300<<(lc+lp))
	fill(lit)
	var isMatch, isRep0Long [192]uint16
	var isRep, isG0, isG1, isG2 [12]uint16
	var slots [4][64]uint16
	var alignProbs [16]uint16
	var posProbs [115]uint16
	for _, p := range [][]uint16{isMatch[:], isRep0Long[:], isRep[:], isG0[:], isG1[:], isG2[:], alignProbs[:], posProbs[:]} {
		fill(p)
	}
	for i := range slots {
		fill(slots[i][:])
	}
	var mainLen, repLen lenDecoder
	mainLen.init()
	repLen.init()
	out = make([]byte, 0, size)
	var rep0, rep1, rep2, rep3, state uint32
	for len(out) < size {
		pos := uint32(len(out))
		posState := pos & (1<<pb - 1)
		if r.bit(&isMatch[state<<4+posState]) == 0 {
			var prev uint32
			if pos > 0 {
				prev = uint32(out[pos-1])
			}
			probs := lit[0x300*((pos&(1<<lp-1))<<lc+prev>>(8-lc)):]
			sym := uint32(1)
			if state >= 7 {
				if rep0 >= pos {
					return nil, errCorrupt
				}
				match := uint32(out[pos-rep0-1])
				for sym < 0x100 {
					mb := match >> 7 & 1
					match <<= 1
					b := r.bit(&probs[(1+mb)<<8+sym])
					sym = sym<<1 | b
					if mb != b {
						break
					}
				}
			}
			for sym < 0x100 {
				sym = sym<<1 | r.bit(&probs[sym])
			}
			out = append(out, byte(sym))
			switch {
			case state < 4:
				state = 0
			case state < 10:
				state -= 3
			default:
				state -= 6
			}
			continue
		}
		var length uint32
		if r.bit(&isRep[state]) != 0 {
			if r.bit(&isG0[state]) == 0 {
				if r.bit(&isRep0Long[state<<4+posState]) == 0 {
					if rep0 >= pos {
						return nil, errCorrupt
					}
					out = append(out, out[pos-rep0-1])
					if state < 7 {
						state = 9
					} else {
						state = 11
					}
					continue
				}
			} else {
				var dist uint32
				if r.bit(&isG1[state]) == 0 {
					dist = rep1
				} else {
					if r.bit(&isG2[state]) == 0 {
						dist = rep2
					} else {
						dist, rep3 = rep3, rep2
					}
					rep2 = rep1
				}
				rep1, rep0 = rep0, dist
			}
			length = repLen.decode(r, posState)
			if state < 7 {
				state = 8
			} else {
				state = 11
			}
		} else {
			rep3, rep2, rep1 = rep2, rep1, rep0
			length = mainLen.decode(r, posState)
			if state < 7 {
				state = 7
			} else {
				state = 10
			}
			lenState := min(length, 3)
			slot := r.tree(slots[lenState][:], 6)
			rep0 = slot
			if slot >= 4 {
				bits := int(slot>>1) - 1
				rep0 = (2 | slot&1) << bits
				if slot < 14 {
					rep0 += r.reverse(posProbs[rep0-slot:], bits)
				} else {
					rep0 += r.direct(bits-4) << 4
					rep0 += r.reverse(alignProbs[:], 4)
				}
			}
			if rep0 == 0xFFFFFFFF {
				break
			}
		}
		length += 2
		if rep0 >= uint32(len(out)) {
			return nil, errCorrupt
		}
		for i := uint32(0); i < length && len(out) < size; i++ {
			out = append(out, out[uint32(len(out))-rep0-1])
		}
	}
	if len(out) != size {
		return nil, errCorrupt
	}
	return out, nil
}
