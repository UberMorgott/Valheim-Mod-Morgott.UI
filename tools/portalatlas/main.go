// portalatlas packs the portal icon sources (NN_name.png, NN = Morgott.Core PortalStyle id 1..32) into one atlas,
// row-major from the top-left, 8 cells per row, cell = source size (128). Mechanical only: copy, no resampling.
// -halo N appends a second block of the same rows below the icons: per icon its alpha dilated by N/2 px, box-blurred
// (N px, 3 passes: a soft wide falloff) and cut out where the icon itself is opaque, RGB white (the arch icon
// tints it in vertex colour).
//
//	cd tools/portalatlas && go run . -src ../../src/Morgott.UI/Assets/PortalIcons -out ../../src/Morgott.UI/Assets/PortalIcons.png -halo 5
package main

import (
	"flag"
	"fmt"
	"image"
	"image/color"
	"image/draw"
	"image/png"
	"os"
	"path/filepath"
	"sort"
)

const cols, cell = 8, 128

func main() {
	src := flag.String("src", "", "folder of NN_name.png")
	out := flag.String("out", "", "atlas png")
	halo := flag.Int("halo", 0, "halo radius in px (0 = no halo block)")
	flag.Parse()
	files, err := filepath.Glob(filepath.Join(*src, "[0-9][0-9]_*.png"))
	check(err)
	sort.Strings(files)
	if len(files) == 0 {
		check(fmt.Errorf("no NN_name.png in %s", *src))
	}
	rows := (len(files) + cols - 1) / cols
	blocks := 1
	if *halo > 0 {
		blocks = 2
	}
	atlas := image.NewNRGBA(image.Rect(0, 0, cols*cell, blocks*rows*cell))
	margin := cell
	for i, f := range files {
		var id int
		if _, err := fmt.Sscanf(filepath.Base(f), "%02d_", &id); err != nil || id != i+1 {
			check(fmt.Errorf("%s: expected id %02d (ids must be contiguous from 01)", f, i+1))
		}
		img := load(f)
		if b := img.Bounds(); b.Dx() != cell || b.Dy() != cell {
			check(fmt.Errorf("%s: %dx%d, want %dx%d", f, b.Dx(), b.Dy(), cell, cell))
		}
		at := image.Pt(i%cols*cell, i/cols*cell)
		draw.Draw(atlas, image.Rectangle{at, at.Add(image.Pt(cell, cell))}, img, img.Bounds().Min, draw.Src)
		if *halo > 0 {
			a := alpha(img)
			margin = min(margin, edgeMargin(a))
			h := haloOf(a, *halo)
			at = at.Add(image.Pt(0, rows*cell))
			draw.Draw(atlas, image.Rectangle{at, at.Add(image.Pt(cell, cell))}, h, image.Point{}, draw.Src)
		}
	}
	w, err := os.Create(*out)
	check(err)
	check(png.Encode(w, atlas))
	check(w.Close())
	fmt.Printf("%s: %d icons, %dx%d", *out, len(files), cols*cell, blocks*rows*cell)
	if *halo > 0 {
		fmt.Printf(", halo %d px, min icon margin %d px", *halo, margin)
	}
	fmt.Println()
}

// alpha of a cell as 0..1, row-major.
func alpha(img image.Image) []float64 {
	b := img.Bounds()
	a := make([]float64, cell*cell)
	for y := 0; y < cell; y++ {
		for x := 0; x < cell; x++ {
			_, _, _, al := img.At(b.Min.X+x, b.Min.Y+y).RGBA()
			a[y*cell+x] = float64(al) / 0xffff
		}
	}
	return a
}

// Distance in px from the cell edge to the nearest pixel with alpha >= 0.1.
func edgeMargin(a []float64) int {
	m := cell
	for y := 0; y < cell; y++ {
		for x := 0; x < cell; x++ {
			if a[y*cell+x] >= 0.1 {
				m = min(m, x, y, cell-1-x, cell-1-y)
			}
		}
	}
	return m
}

func haloOf(a []float64, r int) *image.NRGBA {
	g := r / 2 // grow less, blur wider: soft outer edge
	d := make([]float64, len(a))
	for y := 0; y < cell; y++ {
		for x := 0; x < cell; x++ {
			v := 0.0
			for dy := -g; dy <= g; dy++ {
				for dx := -g; dx <= g; dx++ {
					if dx*dx+dy*dy <= g*g {
						v = max(v, at(a, x+dx, y+dy))
					}
				}
			}
			d[y*cell+x] = v
		}
	}
	for range 3 {
		d = blur(d, r, 1, 0)
		d = blur(d, r, 0, 1)
	}
	out := image.NewNRGBA(image.Rect(0, 0, cell, cell))
	for i, v := range d {
		v *= 1 - a[i] // outline only: the icon's own surface is not brightened
		out.SetNRGBA(i%cell, i/cell, color.NRGBA{255, 255, 255, uint8(min(1, v)*255 + 0.5)})
	}
	return out
}

func blur(a []float64, r, sx, sy int) []float64 {
	o := make([]float64, len(a))
	for y := 0; y < cell; y++ {
		for x := 0; x < cell; x++ {
			s := 0.0
			for k := -r; k <= r; k++ {
				s += at(a, x+k*sx, y+k*sy)
			}
			o[y*cell+x] = s / float64(2*r+1)
		}
	}
	return o
}

func at(a []float64, x, y int) float64 {
	if x < 0 || y < 0 || x >= cell || y >= cell {
		return 0
	}
	return a[y*cell+x]
}

func load(path string) image.Image {
	r, err := os.Open(path)
	check(err)
	defer func() { _ = r.Close() }() // read-only
	img, err := png.Decode(r)
	check(err)
	return img
}

func check(err error) {
	if err != nil {
		fmt.Fprintln(os.Stderr, "portalatlas:", err)
		os.Exit(1)
	}
}
