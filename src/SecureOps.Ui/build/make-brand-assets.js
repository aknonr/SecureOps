// Generates the repository's brand raster assets from the approved emblem master.
//
// Run from the repository root:
//   node src/SecureOps.Ui/build/make-brand-assets.js
//
// Input  : wwwroot/brand/approved/thy-emblem-master.png   (approved corporate emblem, 2000x2000)
// Outputs: wwwroot/brand/approved/thy-emblem-256.png      (transparent, for placement on a light plate)
//          wwwroot/brand/favicon-32.png                   (composited on a white disc)
//          wwwroot/brand/favicon-16.png                   (composited on a white disc)
//          wwwroot/brand/favicon.ico                      (16 + 32 PNG payloads)
//
// The emblem's bird is knocked out rather than painted white, so on a dark surface it would show
// whatever is behind it. The favicon therefore composites it onto a white disc: a browser tab has no
// controllable background, and an emblem whose bird disappears against dark chrome is not an
// identity mark. The 256px transparent copy keeps the knockout for in-app use, where the shell puts
// a white plate behind it deliberately.
//
// No image library. Node's zlib is enough to decode and encode PNG, and adding a build-time
// dependency for four static files would outlive the ten minutes it saves.

const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

const BRAND_DIR = path.join(__dirname, "..", "wwwroot", "brand");
const APPROVED_DIR = path.join(BRAND_DIR, "approved");
const MASTER = path.join(APPROVED_DIR, "thy-emblem-master.png");

// ---- PNG decode -----------------------------------------------------------------------------

function decodePng(buffer) {
    let offset = 8;
    let width = 0;
    let height = 0;
    let colorType = 0;
    let palette = null;
    let alphas = null;
    const chunks = [];

    while (offset < buffer.length) {
        const length = buffer.readUInt32BE(offset);
        const type = buffer.subarray(offset + 4, offset + 8).toString("ascii");
        const data = buffer.subarray(offset + 8, offset + 8 + length);

        if (type === "IHDR") {
            width = data.readUInt32BE(0);
            height = data.readUInt32BE(4);
            colorType = data[9];
        } else if (type === "PLTE") {
            palette = data;
        } else if (type === "tRNS") {
            alphas = data;
        } else if (type === "IDAT") {
            chunks.push(data);
        } else if (type === "IEND") {
            break;
        }

        offset += 12 + length;
    }

    const channels = colorType === 6 ? 4 : colorType === 2 ? 3 : 1;
    const raw = zlib.inflateSync(Buffer.concat(chunks));
    const stride = width * channels + 1;
    const rgba = Buffer.alloc(width * height * 4);
    const line = Buffer.alloc(width * channels);
    const previous = Buffer.alloc(width * channels);

    for (let y = 0; y < height; y++) {
        const filter = raw[y * stride];
        raw.copy(line, 0, y * stride + 1, y * stride + 1 + width * channels);
        unfilter(filter, line, previous, channels);

        for (let x = 0; x < width; x++) {
            const out = (y * width + x) * 4;

            if (colorType === 3) {
                const index = line[x];
                rgba[out] = palette[index * 3];
                rgba[out + 1] = palette[index * 3 + 1];
                rgba[out + 2] = palette[index * 3 + 2];
                rgba[out + 3] = alphas && index < alphas.length ? alphas[index] : 255;
            } else {
                const src = x * channels;
                rgba[out] = line[src];
                rgba[out + 1] = line[src + 1];
                rgba[out + 2] = line[src + 2];
                rgba[out + 3] = channels === 4 ? line[src + 3] : 255;
            }
        }

        line.copy(previous);
    }

    return { width, height, rgba };
}

function unfilter(filter, line, previous, channels) {
    for (let i = 0; i < line.length; i++) {
        const left = i >= channels ? line[i - channels] : 0;
        const up = previous[i];
        const upLeft = i >= channels ? previous[i - channels] : 0;

        switch (filter) {
            case 1: line[i] = (line[i] + left) & 0xff; break;
            case 2: line[i] = (line[i] + up) & 0xff; break;
            case 3: line[i] = (line[i] + ((left + up) >> 1)) & 0xff; break;
            case 4: line[i] = (line[i] + paeth(left, up, upLeft)) & 0xff; break;
            default: break;
        }
    }
}

function paeth(a, b, c) {
    const p = a + b - c;
    const pa = Math.abs(p - a);
    const pb = Math.abs(p - b);
    const pc = Math.abs(p - c);
    return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}

// ---- resample -------------------------------------------------------------------------------

// Box filter over the source region for each destination pixel. Alpha-weighted so the emblem's
// antialiased edge does not darken toward the transparent background as it shrinks.
function resize(image, size) {
    const out = Buffer.alloc(size * size * 4);
    const scale = image.width / size;

    for (let y = 0; y < size; y++) {
        const y0 = Math.floor(y * scale);
        const y1 = Math.min(image.height, Math.ceil((y + 1) * scale));

        for (let x = 0; x < size; x++) {
            const x0 = Math.floor(x * scale);
            const x1 = Math.min(image.width, Math.ceil((x + 1) * scale));

            let r = 0, g = 0, b = 0, a = 0, n = 0;

            for (let sy = y0; sy < y1; sy++) {
                for (let sx = x0; sx < x1; sx++) {
                    const i = (sy * image.width + sx) * 4;
                    const alpha = image.rgba[i + 3];
                    r += image.rgba[i] * alpha;
                    g += image.rgba[i + 1] * alpha;
                    b += image.rgba[i + 2] * alpha;
                    a += alpha;
                    n++;
                }
            }

            const o = (y * size + x) * 4;
            out[o] = a > 0 ? Math.round(r / a) : 0;
            out[o + 1] = a > 0 ? Math.round(g / a) : 0;
            out[o + 2] = a > 0 ? Math.round(b / a) : 0;
            out[o + 3] = Math.round(a / n);
        }
    }

    return { width: size, height: size, rgba: out };
}

// Places the emblem on an opaque white disc. Outside the disc stays transparent so the icon keeps
// its round silhouette instead of becoming a white square in the tab strip.
function onWhiteDisc(image) {
    const { width: size, rgba } = image;
    const out = Buffer.alloc(rgba.length);
    const centre = (size - 1) / 2;
    const radius = size / 2;

    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            const i = (y * size + x) * 4;
            const distance = Math.hypot(x - centre, y - centre);
            // One pixel of feather keeps the circle edge smooth at 16px.
            const disc = Math.max(0, Math.min(1, radius - distance));
            const fg = rgba[i + 3] / 255;

            const r = rgba[i] * fg + 255 * (1 - fg);
            const g = rgba[i + 1] * fg + 255 * (1 - fg);
            const b = rgba[i + 2] * fg + 255 * (1 - fg);

            out[i] = Math.round(r);
            out[i + 1] = Math.round(g);
            out[i + 2] = Math.round(b);
            out[i + 3] = Math.round(255 * disc);
        }
    }

    return { width: size, height: size, rgba: out };
}

// ---- PNG encode -----------------------------------------------------------------------------

function encodePng(image) {
    const { width, height, rgba } = image;
    const raw = Buffer.alloc(height * (width * 4 + 1));

    for (let y = 0; y < height; y++) {
        raw[y * (width * 4 + 1)] = 0;
        rgba.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
    }

    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0);
    ihdr.writeUInt32BE(height, 4);
    ihdr[8] = 8;
    ihdr[9] = 6;

    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk("IHDR", ihdr),
        chunk("IDAT", zlib.deflateSync(raw, { level: 9 })),
        chunk("IEND", Buffer.alloc(0))
    ]);
}

function chunk(type, data) {
    const header = Buffer.alloc(8);
    header.writeUInt32BE(data.length, 0);
    header.write(type, 4, "ascii");

    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(Buffer.concat([Buffer.from(type, "ascii"), data])), 0);

    return Buffer.concat([header, data, crc]);
}

const CRC_TABLE = (() => {
    const table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) {
            c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        }
        table[n] = c;
    }
    return table;
})();

function crc32(buffer) {
    let c = 0xffffffff;
    for (let i = 0; i < buffer.length; i++) {
        c = CRC_TABLE[(c ^ buffer[i]) & 0xff] ^ (c >>> 8);
    }
    return (c ^ 0xffffffff) >>> 0;
}

// ---- ICO container --------------------------------------------------------------------------

// PNG-compressed entries. Every browser that reaches this application supports them, and the
// uncompressed DIB alternative triples the file for no benefit.
function encodeIco(entries) {
    const header = Buffer.alloc(6);
    header.writeUInt16LE(0, 0);
    header.writeUInt16LE(1, 2);
    header.writeUInt16LE(entries.length, 4);

    const directory = [];
    let offset = 6 + entries.length * 16;

    for (const entry of entries) {
        const record = Buffer.alloc(16);
        record[0] = entry.size === 256 ? 0 : entry.size;
        record[1] = entry.size === 256 ? 0 : entry.size;
        record[4] = 1;
        record.writeUInt16LE(32, 6);
        record.writeUInt32LE(entry.png.length, 8);
        record.writeUInt32LE(offset, 12);
        directory.push(record);
        offset += entry.png.length;
    }

    return Buffer.concat([header, ...directory, ...entries.map(e => e.png)]);
}

// ---- run ------------------------------------------------------------------------------------

const master = decodePng(fs.readFileSync(MASTER));
console.log(`master ${master.width}x${master.height}`);

const emblem256 = resize(master, 256);
write(path.join(APPROVED_DIR, "thy-emblem-256.png"), encodePng(emblem256));

const favicon32 = onWhiteDisc(resize(master, 32));
const favicon16 = onWhiteDisc(resize(master, 16));
write(path.join(BRAND_DIR, "favicon-32.png"), encodePng(favicon32));
write(path.join(BRAND_DIR, "favicon-16.png"), encodePng(favicon16));

write(path.join(BRAND_DIR, "favicon.ico"), encodeIco([
    { size: 16, png: encodePng(favicon16) },
    { size: 32, png: encodePng(favicon32) }
]));

function write(target, buffer) {
    fs.writeFileSync(target, buffer);
    console.log(`${path.relative(process.cwd(), target)}  ${(buffer.length / 1024).toFixed(1)} KB`);
}
