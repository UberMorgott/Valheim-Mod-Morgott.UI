// portalatlas packs the portal icon sources (NN_name.png, NN = Morgott.Core PortalStyle id 1..32) into one atlas,
// row-major from the top-left, 8 cells per row, cell = source size (128). Mechanical only: copy, no resampling.
//
//	go run ./tools/portalatlas -src src/Morgott.UI/Assets/PortalIcons -out src/Morgott.UI/Assets/PortalIcons.png
package main

import (
	"flag"
	"fmt"
	"image"
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
	flag.Parse()
	files, err := filepath.Glob(filepath.Join(*src, "[0-9][0-9]_*.png"))
	check(err)
	sort.Strings(files)
	if len(files) == 0 {
		check(fmt.Errorf("no NN_name.png in %s", *src))
	}
	rows := (len(files) + cols - 1) / cols
	atlas := image.NewNRGBA(image.Rect(0, 0, cols*cell, rows*cell))
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
	}
	w, err := os.Create(*out)
	check(err)
	check(png.Encode(w, atlas))
	check(w.Close())
	fmt.Printf("%s: %d icons, %dx%d\n", *out, len(files), cols*cell, rows*cell)
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
