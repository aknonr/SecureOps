// Generates the dot-matrix world map used by the sign-in field and the flight loading card.
//
// Run from the repository root:
//   node src/SecureOps.Ui/build/make-world-map.js
//
// Output: wwwroot/brand/world-dots.svg
//
// Why a generator rather than a hand-drawn SVG: the geography is authored below in longitude and
// latitude, which is the only form a human can reason about and correct. Pixel path data for a
// world map is unreviewable — nobody can look at a `d` attribute and say whether the Horn of Africa
// is in the right place. Polygons in degrees can be checked against an atlas and adjusted.
//
// Why dots rather than filled outlines: it is the language of the approved references, it reads as
// "earth at night" without tracing anyone's map artwork, and it stays legible when scaled down into
// a loading card. Precision is deliberately coarse — this is visual geography, not GIS.

const fs = require("fs");
const path = require("path");

// Equirectangular window: western Atlantic to the Pacific, Arctic to southern Africa. Chosen so
// Türkiye — the hub every route radiates from — sits near the optical centre of the field.
const LON_MIN = -25, LON_MAX = 155;
const LAT_MIN = -38, LAT_MAX = 72;
const WIDTH = 1000, HEIGHT = 660;

const x = lon => ((lon - LON_MIN) / (LON_MAX - LON_MIN)) * WIDTH;
const y = lat => ((LAT_MAX - lat) / (LAT_MAX - LAT_MIN)) * HEIGHT;

// Coarse landmass outlines in [lon, lat]. Enough vertices to be recognisable, few enough to stay
// correctable by hand.
const LAND = {
    africa: [
        [-9.5, 35.8], [0, 36.5], [10, 37], [20, 32.5], [25, 31.5], [32, 31.5],
        [35, 28], [37, 22], [39, 15], [43, 12], [48, 11.5], [51.4, 10.4],
        [48, 6], [44, 3], [41, -2], [40, -8], [40, -15], [35, -20], [33, -26],
        [30, -30], [26, -33.5], [20, -34.8], [17, -32], [14, -26], [12, -18],
        [13, -12], [12, -6], [9, -1], [9.5, 4], [5, 5.5], [0, 5.5], [-5, 5],
        [-8, 6.5], [-13, 8.5], [-16, 12], [-17.5, 14.7], [-16, 20], [-13, 26],
        [-10, 30], [-9.5, 35.8]
    ],
    eurasia: [
        // Iberia and the Atlantic face of Europe.
        [-9.5, 36.5], [-9, 41], [-8, 43.5], [-2, 43.5], [-1.5, 46], [-4, 48.5],
        [1, 50], [4, 52], [4.5, 53.5], [8, 54], [8, 57], [10, 58],
        // Scandinavia up to the Arctic.
        [5, 59.5], [5, 62], [11, 64], [15, 68], [21, 70], [28, 71],
        // The Russian Arctic seaboard.
        [40, 68], [55, 71], [70, 72], [90, 74], [110, 74], [130, 72], [145, 70],
        [155, 68],
        // Pacific face down to the South China Sea.
        [155, 60], [150, 52], [143, 48], [135, 43], [130, 42], [128, 37],
        [122, 32], [118, 25], [110, 21], [107, 15], [109, 11], [104, 9],
        [103.8, 1.3],
        // Back up the Malay peninsula, then Indochina and the Bay of Bengal.
        [100, 6], [98, 12], [94, 16], [90, 21], [87, 21],
        // India.
        [83, 17], [80.3, 13], [77.5, 8], [75, 12], [73, 18], [72.8, 20],
        [70, 22], [68, 24], [67, 25],
        // Arabia and the Gulf.
        [61, 25], [57, 25], [56, 26.5], [52, 24], [50, 20], [45, 12.8],
        [43, 12.6], [40, 16], [37, 22], [35, 27], [34, 31.5],
        // The Levant and the northern shore of the Mediterranean.
        [36, 36], [30, 36.5], [26, 38.5], [23, 37], [20, 39.5], [16, 41],
        [18, 40], [15, 38], [12, 38], [10, 44], [7, 44], [3, 42.5],
        [-1, 39], [-6, 36.5], [-9.5, 36.5]
    ],
    britain: [
        [-5, 50], [-3, 51.5], [1.5, 51], [0, 53], [-1, 55], [-3, 58.5],
        [-5, 58], [-6, 55], [-4.5, 53], [-5, 50]
    ],
    ireland: [[-10, 51.5], [-6, 52], [-6, 55], [-10, 54.5], [-10, 51.5]],
    madagascar: [[43, -12], [50, -15], [50, -25], [45, -25], [43, -20], [43, -12]],
    srilanka: [[80, 6], [82, 7], [81.5, 9.5], [79.8, 9], [80, 6]],
    japan: [
        [130, 31], [135, 34], [140, 36], [142, 40], [141, 45], [144, 44],
        [140, 41], [137, 37], [132, 33], [130, 31]
    ],
    sumatra: [[95, 5.5], [100, 2], [105, -6], [102, -6], [96, 2], [95, 5.5]],
    java: [[105, -6], [114, -7], [114, -8.5], [105, -7.5], [105, -6]],
    borneo: [[109, 2], [117, 4], [119, -1], [116, -4], [110, -3], [109, 2]],
    newguinea: [[131, -1], [145, -3], [150, -8], [141, -9], [133, -4], [131, -1]],
    iceland: [[-24, 64], [-14, 64.5], [-14, 66.5], [-22, 66], [-24, 64]]
};

// Even-odd ray casting. Coarse polygons, so a point either falls inside a continent or it does not.
function inside(px, py, polygon) {
    let hit = false;
    for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
        const [xi, yi] = polygon[i];
        const [xj, yj] = polygon[j];
        if ((yi > py) !== (yj > py) && px < ((xj - xi) * (py - yi)) / (yj - yi) + xi) {
            hit = !hit;
        }
    }
    return hit;
}

// Inland and enclosed seas, subtracted after the landmasses. Without them the coarse continent
// polygons close the Mediterranean and Europe fuses to Africa — which destroys the one thing this
// map has to communicate, that Türkiye sits between the two.
const SEA = {
    // Traced as a simple band rather than a coastline. A faithful outline self-intersects at this
    // vertex count and the even-odd fill then leaves the sea closed, which is the one failure this
    // polygon exists to prevent.
    mediterranean: [
        [-6, 35.0], [-6, 37.0], [3, 40.5], [12, 42.5], [16, 40.0], [19, 41.5],
        [24, 41.0], [29, 38.5], [36, 37.0], [36, 34.5], [30, 31.0], [20, 32.0],
        [10, 34.0], [0, 35.5], [-6, 35.0]
    ],
    blacksea: [[28, 41], [40, 42], [41, 45], [37, 47], [31, 46.5], [28, 44], [28, 41]],
    caspian: [[47, 37], [54, 41], [53, 46], [49, 46], [47, 42], [47, 37]],
    redsea: [[33, 28], [36, 27], [43, 13], [44.5, 12.3], [42, 12.5], [38, 20], [34.5, 25], [33, 28]],
    gulf: [[48, 30], [56, 26.5], [57, 25.2], [53, 27], [49, 29], [48, 30]],
    baltic: [[13, 54], [21, 56], [25, 60], [22, 63], [18, 60], [14, 56], [13, 54]],
    northsea: [[0, 52], [8, 54], [8, 57], [4, 59], [-1, 58], [-2, 54], [0, 52]],
    biscay: [[-6, 44], [-1.5, 44], [-1.5, 46.5], [-4.5, 48], [-8, 46], [-6, 44]],
    persia: [[46, 33], [50, 33], [50, 36], [46, 36], [46, 33]],
    aral: [[58, 44], [62, 44], [62, 46.5], [58, 46.5], [58, 44]]
};

const polygons = Object.values(LAND);
const seas = Object.values(SEA);
const isLand = (lon, lat) =>
    polygons.some(p => inside(lon, lat, p)) && !seas.some(s => inside(lon, lat, s));

// Grid step in degrees. Finer than this and the SVG grows without reading any better; coarser and
// the Mediterranean closes up.
const STEP_LON = 1.6, STEP_LAT = 1.6;

const dots = [];
for (let lat = LAT_MAX; lat >= LAT_MIN; lat -= STEP_LAT) {
    for (let lon = LON_MIN; lon <= LON_MAX; lon += STEP_LON) {
        if (!isLand(lon, lat)) {
            continue;
        }
        dots.push([x(lon).toFixed(1), y(lat).toFixed(1)]);
    }
}

// One path of tiny squares rather than thousands of <circle> elements: roughly a third of the bytes
// and one node for the renderer to deal with. At this size a square and a circle are the same dot.
const r = 1.6;
const d = dots.map(([cx, cy]) => `M${cx} ${cy}h${r}v${r}h-${r}z`).join("");

const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${WIDTH} ${HEIGHT}" width="${WIDTH}" height="${HEIGHT}" role="presentation" aria-hidden="true" focusable="false"><path fill="currentColor" d="${d}"/></svg>`;

const target = path.join(__dirname, "..", "wwwroot", "brand", "world-dots.svg");
fs.writeFileSync(target, svg);
console.log(`${dots.length} dots -> ${path.relative(process.cwd(), target)}  ${(svg.length / 1024).toFixed(1)} KB`);
