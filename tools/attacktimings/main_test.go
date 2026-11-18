package main

import "testing"

func TestLz4OverlappingMatch(t *testing.T) {
	// literal "ab", then a 6 byte match at offset 2, then the mandatory final literal "c"
	got, err := lz4Block([]byte{0x22, 'a', 'b', 2, 0, 0x10, 'c'}, 9)
	if err != nil || string(got) != "ababababc" {
		t.Fatalf("got %q, %v", got, err)
	}
}

func TestRoundMatchesPython(t *testing.T) {
	for _, c := range []struct {
		v    float64
		n    int
		want float64
	}{{0.6919, 3, 0.692}, {2.675, 2, 2.67}, {1.15, 1, 1.1}} {
		if got := round(c.v, c.n); got != c.want {
			t.Errorf("round(%v, %d) = %v, want %v", c.v, c.n, got, c.want)
		}
	}
}

func TestReplaceBody(t *testing.T) {
	src := "a Dictionary<string, float> T = x\n        {\n            old,\n        };\nz\n"
	got, err := replaceBody(src, "Dictionary<string, float> T", []string{"            new,"})
	want := "a Dictionary<string, float> T = x\n        {\n            new,\n        };\nz\n"
	if err != nil || got != want {
		t.Fatalf("got %q, %v", got, err)
	}
}
