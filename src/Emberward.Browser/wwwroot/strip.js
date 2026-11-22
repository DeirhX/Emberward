// The desktop's bottom HUD (HudStrip.cs and the gauges it draws): the TEMP, HULL and MATERIALS art in the
// bottom-left corner, BUILD STRUCTURES and COMMAND in the bottom-right, the middle of the view left clear. It draws
// the desktop's own art (copied in by tools/browser/publish.py) on two canvases, at the desktop's art coordinates,
// so every number below is the desktop's. Shown on wide screens with a mouse; phones keep the plates (hud.js).
//
// It is drawn after each step, 24 times a second, and not while the game is paused. What never changes (the art,
// the panel titles, the orb's glass, the dial's dim track and its ticks) is drawn once for each size into layers
// kept off screen, so a step costs one image per canvas and the moving parts; nothing is blurred. The desktop eases
// its gauges once a display frame; here they ease once a step, by as much as the desktop's do over a step.

// HudLayout.cs: the art's own size, and where each panel starts along it.
const STRIP_H = 223,
    HEAT_W = 245,
    SHIELD_W = 145,
    MATERIALS_W = 230,
    BUILD_W = 404;
const HEAT_X = 0,
    SHIELD_X = HEAT_X + HEAT_W,
    MATERIALS_X = SHIELD_X + SHIELD_W,
    BUILD_X = MATERIALS_X + MATERIALS_W;
const LEFT_W = BUILD_X,
    RIGHT_W = BUILD_W,
    MIN_CENTER_GAP = 404,
    REFERENCE_HEIGHT = 1080,
    // Texture pixels per art pixel: the PNGs are drawn at twice their layout size.
    ART_DENSITY = 2;
// Room above the left part for the OFF NETWORK tag, which the desktop draws 24 art pixels above TEMP.
const HEADROOM = 44;

// HudStrip.cs: the plates' centres, title rows and readout rows.
const TEMP = { x: HEAT_X + 170, title: 151.5, readout: 179, right: HEAT_X + 239 },
    SHIELD = { x: SHIELD_X + 80.5, title: 153.5, readout: 180 },
    MATERIALS = { x: MATERIALS_X + 120, title: 154 };
// HudStrip.DrawLedger: one line in the readout plate, SHIELD 400 | 1000 HULL, the numbers meeting at the split and a
// hairline bar under each half, filled from the plate's outer edge. LedgerLayout.cs places it so nothing overlaps or
// leaves the plate.
const LEDGER = {
    left: SHIELD_X + 15, right: SHIELD_X + 144, split: SHIELD_X + 79.5, y: 178.5,
    barDrop: 9.5, barHalf: 0.9, barOutline: 0.6, barGap: 1.5, label: 12, number: 15,
};
// LedgerLayout.SplitGap and SplitHalf: the gap between a value and the split, and the split's hairline half height.
const SPLIT_GAP = 3.5,
    SPLIT_HALF = 4.5,
    SPLIT_COLOR = 0x60a0b4c8;
// HudPalette: the shield's cyan, an empty shield's slate, the hull label's warm colour (ABGR).
const SHIELD_CYAN = 0xffffe0a0,
    SHIELD_SLATE = 0xff997f5f,
    HULL_NEUTRAL = 0xff70b8ff;
const ORE_LINE = { x: MATERIALS_X + 122, y: 133, size: 15 };
// LedgerLayout.cs and HudStrip.DrawMaterials: the MATERIALS plate's one line (no bars), HOLD 23 | 1,234 +18/m BASE.
const MATERIALS_LEDGER = {
    left: MATERIALS_X + 35, split: MATERIALS_X + 87, rateRight: MATERIALS_X + 175, right: MATERIALS_X + 205, y: 184,
    label: 12, number: 15, rate: 12, deltaRise: 14,
};
// HudStrip's MATERIALS colours (ABGR): labels, rate, the store and its deposit flash.
const MATERIALS_LABEL = 0xff9ac8e8,
    MATERIALS_RATE = 0xff5aa0c8,
    STORE_COLOR = 0xffb8f0ff,
    STORE_FLASH = 0xff40c4ff;

// HeatGauge.cs
const ORB = { x: HEAT_X + 168, y: 72, r: 47 };
const DIAL_START = (Math.PI * 150) / 180,
    DIAL_SWEEP = (Math.PI * 240) / 180;

// LiquidGauges.cs: the outline of the shield crystal.
const SHIELD_OUTLINE = [
    [44, 44], [60, 40], [82, 37], [104, 40], [121, 44], [121, 80],
    [114, 100], [100, 116], [82, 128], [64, 116], [50, 100], [44, 80],
].map(([x, y]) => [SHIELD_X + x, y]);

// BuildPanel.cs: the slots inside build_panel.png, three columns by two rows.
const SLOT_X = [25, 117, 209],
    SLOT_Y = [44, 105],
    SLOT_W = 80,
    SLOT_H = 56;
const COMMAND = { x0: BUILD_X + 77, y0: 172, x1: BUILD_X + 277, y1: 212 };

// The desktop's per-frame easing (at 60 frames a second) over one step (24 a second).
const perStep = (k) => 1 - Math.pow(1 - k, 60 / 24);
const EASE_HEAT = perStep(0.15),
    EASE_SHIELD = perStep(0.2);

const FONT = '"Chakra Petch", system-ui, sans-serif';
// ImGui sizes a font by its line's height (ascent to descent) and CSS by its em, so the desktop's sizes are scaled
// by the font's em over its line height once it has loaded.
let fontScale = 1;
function measureFontScale() {
    const ctx = document.createElement("canvas").getContext("2d");
    ctx.font = `500 100px ${FONT}`;
    const m = ctx.measureText("Hg");
    const line = m.fontBoundingBoxAscent + m.fontBoundingBoxDescent;
    if (line > 0) fontScale = 100 / line;
}
const font = (size) => `500 ${(size * fontScale).toFixed(2)}px ${FONT}`;
const ART = {
    heat: "hud/heat_icon.png",
    shield: "hud/shield_icon.png",
    materials: "hud/panel_frame.png",
    orePile: "hud/hold_pyrite.png",
    build: "hud/build_panel.png",
};

// An ImGui colour as the desktop writes it (0xAABBGGRR) in CSS.
function abgr(u) {
    const r = u & 0xff,
        g = (u >>> 8) & 0xff,
        b = (u >>> 16) & 0xff,
        a = (u >>> 24) / 255;
    return `rgba(${r},${g},${b},${a.toFixed(3)})`;
}

const BUILD_STATE_BORDER = {
    Ready: abgr(0xff40ff60),
    Filling: abgr(0xff707070),
    Blocked: abgr(0xff707070),
};
const CAPTION_COLOR = {
    FromBase: abgr(0xfff0d080),
    FromHold: abgr(0xff78e6ff),
    KeyAndName: abgr(0xffb0b0b0),
};

// HudLayout.HudScale and ArtScale: art pixels to CSS pixels, enlarged above a 1080-high window, shrunk only when the
// window is too narrow for both parts and the gap between them, and snapped to whole device pixels where that keeps
// the art from being resampled.
export function stripScale(width, height, density) {
    const gap = Math.min(MIN_CENTER_GAP, width * 0.5);
    const fit = Math.max(0, width - gap) / (LEFT_W + RIGHT_W);
    const scale = Math.min(fit, Math.max(1, height / REFERENCE_HEIGHT));
    if (density <= 1) return scale;
    const exact = scale * density;
    return exact < ART_DENSITY ? scale : Math.floor(exact) / density;
}

export class StripView {
    constructor(input, onCommand) {
        this.left = document.getElementById("strip-left");
        this.right = document.getElementById("strip-right");
        this.input = input;
        this.art = {};
        this.loading = null;
        this.frame = null;
        this.dial = null;
        // The layers that do not change (buildLayers), and the size they were drawn for.
        this.layers = null;
        this.layersKey = "";
        this.scale = 0;
        this.density = 0;
        this.reset();
        this.right.addEventListener("pointerdown", (e) => {
            const p = this.artPoint(this.right, e, BUILD_X, 0);
            if (!p) return;
            e.preventDefault();
            const col = SLOT_X.findIndex(
                (x) => p.x >= BUILD_X + x && p.x < BUILD_X + x + SLOT_W,
            );
            const row = SLOT_Y.findIndex((y) => p.y >= y && p.y < y + SLOT_H);
            if (col >= 0 && row >= 0) {
                const build = this.frame?.builds.find((b) => b.slot === row * SLOT_X.length + col);
                if (build) input.queue("build", build.key);
            } else if (
                p.x >= COMMAND.x0 && p.x < COMMAND.x1 &&
                p.y >= COMMAND.y0 && p.y < COMMAND.y1
            )
                onCommand();
        });
        // The strip is part of the HUD: a click on it works the HUD and does not fire.
        this.left.addEventListener("pointerdown", (e) => e.preventDefault());
    }

    // Forgets what the last match showed: the next frame starts the gauges where they stand.
    reset() {
        this.shown = { heat: NaN, crystal: -1 };
        this.lastHeat = NaN;
        this.trend = 0;
    }

    // Loads the art once, the first time the strip is shown.
    load() {
        this.loading ||= Promise.all(
            Object.entries(ART).map(
                ([name, src]) =>
                    new Promise((resolve, reject) => {
                        const image = new Image();
                        image.onload = () => resolve([name, image]);
                        image.onerror = () => reject(new Error(`HUD art ${src} did not load`));
                        image.src = src;
                    }),
            ),
        ).then(async (images) => {
            this.art = Object.fromEntries(images);
            await document.fonts.load(`20px ${FONT}`);
            measureFontScale();
            if (this.frame) this.draw(this.frame, this.time);
        });
        this.loading.catch((error) => console.warn(error));
    }

    // Where a pointer event lies in art pixels, for a canvas whose left edge is at art x `originX`.
    artPoint(canvas, e, originX, top) {
        if (!this.scale) return null;
        const r = canvas.getBoundingClientRect();
        return {
            x: originX + (e.clientX - r.left) / this.scale,
            y: (e.clientY - r.top) / this.scale - top,
        };
    }

    // Sizes the two canvases for the window. Returns the strip's height in CSS pixels.
    layout(width, height) {
        const density = Math.min(window.devicePixelRatio || 1, 3);
        const scale = stripScale(width, height, density);
        if (scale !== this.scale || density !== this.density) {
            this.scale = scale;
            this.density = density;
            size(this.left, LEFT_W * scale, (STRIP_H + HEADROOM) * scale, density);
            size(this.right, RIGHT_W * scale, STRIP_H * scale, density);
            if (this.frame) this.draw(this.frame, this.time);
        }
        return STRIP_H * scale;
    }

    // Takes in the HUD state after a step, and draws it when `visible`. It keeps up while the strip is hidden: the
    // heat dial comes with a match's first frame only, and a window that grows into the wide layout later
    // (layout) draws at once from the last frame.
    update(f, time, visible = true) {
        if (f.dial) this.dial = f.dial;
        const p = f.player;
        // A dead tank's orb sinks to the cold end, as on the desktop.
        const heat = p.dead && this.dial ? this.dial.min : p.heat;
        this.shown.heat = Number.isNaN(this.shown.heat)
            ? heat
            : this.shown.heat + (heat - this.shown.heat) * EASE_HEAT;
        // The crystal holds the shield when the match has one, else the hull; a dead tank's shield is spent.
        const crystal = p.shieldActive ? (p.dead ? 0 : p.shieldFill) : p.hull;
        this.shown.crystal =
            this.shown.crystal < 0
                ? crystal
                : this.shown.crystal + (crystal - this.shown.crystal) * EASE_SHIELD;
        // The trend arrow: the desktop smooths the change per display frame and shows it past 0.02 a frame.
        if (!Number.isNaN(this.lastHeat) && !p.dead)
            this.trend = this.trend * 0.9 + ((p.heat - this.lastHeat) * 24) / 60 * 0.1;
        this.lastHeat = p.heat;
        this.frame = f;
        this.time = time;
        if (visible) this.draw(f, time);
    }

    draw(f, time) {
        if (!this.art.build || !this.scale || !this.dial) return;
        const layers = this.buildLayers();
        const p = f.player;
        const k = this.scale * this.density;
        const left = this.left.getContext("2d");
        left.setTransform(1, 0, 0, 1, 0, 0);
        left.clearRect(0, 0, this.left.width, this.left.height);
        left.drawImage(layers.left, 0, 0);
        left.setTransform(k, 0, 0, k, 0, HEADROOM * k);
        const right = this.right.getContext("2d");
        right.setTransform(1, 0, 0, 1, 0, 0);
        right.clearRect(0, 0, this.right.width, this.right.height);
        right.drawImage(layers.right, 0, 0);
        right.setTransform(k, 0, 0, k, -BUILD_X * k, 0);

        this.drawOrb(left, p, time);
        this.drawLiquids(left, p, time);
        // The plate's title is one status word (HullStatuses), so it is drawn each frame, not in the cached layer.
        const plate = p.plate;
        const flash = plate.wordFlashColor !== plate.wordColor && Math.floor(time * 4) % 2 !== 0;
        stripText(left, plate.word, SHIELD.x, SHIELD.title, 17, flash ? plate.wordFlashColor : plate.wordColor, plate.wordColor + "30");

        const readout = (text, plate) =>
            stripText(left, text, plate.x, plate.readout, 22, abgr(0xffaafacd), abgr(0x2830ff60));
        readout(`${p.dead ? 0 : Math.round(p.heat)}°`, TEMP);
        this.drawTrend(left, p);
        this.drawLedger(left, p);
        const m = p.materials;
        if (m.pileFrame >= 0) {
            const column = m.pileFrame % 8, row = Math.floor(m.pileFrame / 8);
            left.drawImage(this.art.orePile, column * 288, row * 120, 288, 120, MATERIALS_X + 51, 48, 144, 60);
        }
        drawMaterials(left, m);

        const oreColor = m.unloading ? 0xff60ff90 : m.holdFull ? 0xff2880ff : 0xff78e6ff;
        stripText(left, m.oreLine, ORE_LINE.x, ORE_LINE.y, ORE_LINE.size, abgr(oreColor), abgr(0x2840b0ff));
        if (m.unloading) {
            // Down arrows either side of the word, bobbing: the ore is going down into the base.
            const bob = ((time * 3) % 1) * 5;
            left.fillStyle = abgr(oreColor);
            for (const dx of [-55, 55]) {
                const cx = ORE_LINE.x + dx,
                    top = ORE_LINE.y - 5 + bob;
                triangle(left, cx - 4, top, cx + 4, top, cx, top + 5);
            }
        }
        this.drawOffNetwork(left, p, time, f.reserve);

        this.drawBuild(right, f.builds, p, time);
        // The COMMAND button stands for the base upgrades: lit while docked (keys 1-7 buy), dimmed elsewhere.
        if (!p.docked) {
            right.fillStyle = abgr(0x90000000);
            roundRect(right, COMMAND.x0, COMMAND.y0, COMMAND.x1, COMMAND.y1, 10);
            right.fill();
        } else {
            right.strokeStyle = abgr(Math.floor(time * 2) % 2 === 0 ? 0xff40ff60 : 0xff208030);
            right.lineWidth = 2.5;
            roundRect(right, COMMAND.x0, COMMAND.y0, COMMAND.x1, COMMAND.y1, 10);
            right.stroke();
        }
    }

    // What never changes, drawn once for this size: the two canvases' backgrounds (art, titles, the orb's glass and
    // the dial's dim track, the empty second row of build slots), the dial lit all the way round and the dial's
    // ticks and glass highlight, the last two in sprites the size of the orb.
    buildLayers() {
        const key = `${this.scale}x${this.density}`;
        if (this.layers && this.layersKey === key) return this.layers;
        const k = this.scale * this.density;
        const { x, y, r } = ORB;
        const dial = this.dial;

        const [leftLayer, left] = offscreen(this.left.width, this.left.height, k, 0, HEADROOM * k);
        for (const [image, px, w] of [
            [this.art.heat, HEAT_X, HEAT_W],
            [this.art.shield, SHIELD_X, SHIELD_W],
            [this.art.materials, MATERIALS_X, MATERIALS_W],
        ])
            left.drawImage(image, px, 0, w, STRIP_H);
        stripText(left, "TEMP", TEMP.x, TEMP.title, 19, abgr(0xff78c8ff), abgr(0x181880ff));
        stripText(left, "MATERIALS", MATERIALS.x, MATERIALS.title, 21, abgr(0xff68e0ff), abgr(0x282090ff));
        // Dark glass behind the liquid (hides the energy bolt baked into the art), and the dial's dim track.
        left.fillStyle = abgr(0xff0a0c0e);
        circle(left, x, y, r - 1);
        left.fill();
        strokeDial(left, dial, "48");

        const [rightLayer, right] = offscreen(this.right.width, this.right.height, k, -BUILD_X * k, 0);
        right.drawImage(this.art.build, BUILD_X, 0, BUILD_W, STRIP_H);
        stripText(right, "BUILD STRUCTURES", BUILD_X + 162, 23.5, 17, abgr(0xffb0e4f8), abgr(0x181880ff));
        stripText(right, "COMMAND", BUILD_X + 171, 191.5, 21, abgr(0xff98e8ff), abgr(0x202080ff));
        // The second row of slots has no machines yet: dark.
        right.fillStyle = abgr(0xa8000000);
        for (const sx of SLOT_X) {
            roundRect(right, BUILD_X + sx, SLOT_Y[1], BUILD_X + sx + SLOT_W, SLOT_Y[1] + SLOT_H, 6);
            right.fill();
        }

        // Sprites the size of the orb, drawn in art coordinates.
        const side = Math.ceil((2 * r + 8) * k);
        const sprite = () => offscreen(side, side, k, (r + 4 - x) * k, (r + 4 - y) * k);
        const [litLayer, lit] = sprite();
        strokeDial(lit, dial, "ff");
        const [overLayer, over] = sprite();
        // Ticks at ambient and at the safe maximum, where overheating starts.
        const arcR = r - 6,
            arcW = 5;
        for (const [t, color] of [[dial.ambient, "rgba(255,255,255,0.75)"], [dial.safe, abgr(0xff4060ff)]]) {
            const a = DIAL_START + DIAL_SWEEP * t;
            const dx = Math.cos(a),
                dy = Math.sin(a);
            line(over, x, y, dx, dy, arcR - arcW / 2 - 3, arcR + arcW / 2 + 1, "#000", 4);
            line(over, x, y, dx, dy, arcR - arcW / 2 - 2, arcR + arcW / 2, color, 2);
        }
        // A highlight on the glass.
        over.strokeStyle = abgr(0x30ffffff);
        over.lineWidth = 3;
        over.beginPath();
        over.arc(x, y, r - 12, Math.PI * 1.15, Math.PI * 1.55);
        over.stroke();

        this.layersKey = key;
        this.layers = {
            left: leftLayer,
            right: rightLayer,
            lit: litLayer,
            over: overLayer,
            // Where the sprites go, in art pixels.
            sprite: [x - r - 4, y - r - 4, side / k, side / k],
        };
        return this.layers;
    }

    // The colour of the dial's band at a point of the heat scale (HeatScale.ColorRgb at the band's middle).
    bandColor(fraction) {
        const bands = this.dial.bands;
        return bands[Math.min(bands.length - 1, Math.max(0, Math.floor(fraction * bands.length)))];
    }

    // HeatGauge.DrawOrb: a glass sphere of liquid in the temperature's colour, frozen in the cold, bubbling as it
    // heats, boiling while overheating burns the shield; round it a dial lit up to the temperature, with a needle.
    // The desktop draws the liquid with a shader (HeatOrbPs); this is the same idea in canvas strokes.
    drawOrb(ctx, p, time) {
        const { x, y, r } = ORB;
        const alive = !p.dead;
        const dial = this.dial;
        // HeatScale.Fraction of the eased temperature.
        const lit = alive ? Math.min(1, Math.max(0, (this.shown.heat - dial.min) / (dial.max - dial.min))) : 0;
        const pulse = 0.5 + 0.5 * Math.sin(time * 10);

        const layers = this.layers;
        // The liquid, over the glass in the background layer.
        const inner = r - 9;
        const liquid = alive ? this.bandColor(lit) : "#1f2124";
        const body = ctx.createRadialGradient(x - inner * 0.3, y - inner * 0.35, inner * 0.1, x, y, inner);
        body.addColorStop(0, mix(liquid, "#ffffff", 0.35));
        body.addColorStop(0.55, liquid);
        body.addColorStop(1, mix(liquid, "#000000", 0.6));
        ctx.fillStyle = body;
        circle(ctx, x, y, inner);
        ctx.fill();
        ctx.save();
        circle(ctx, x, y, inner);
        ctx.clip();
        if (alive && p.frozen > 0) {
            // Ice: a pale film and cracks that spread as it gets colder.
            ctx.fillStyle = `rgba(220,240,255,${(0.45 * p.frozen).toFixed(3)})`;
            ctx.fillRect(x - inner, y - inner, inner * 2, inner * 2);
            ctx.strokeStyle = `rgba(255,255,255,${(0.7 * p.frozen).toFixed(3)})`;
            ctx.lineWidth = 1.2;
            ctx.beginPath();
            for (let i = 0; i < 6; i++) {
                const a = i * 1.07 + 0.4;
                const len = inner * (0.35 + 0.6 * p.frozen) * (0.6 + 0.4 * ((i * 37) % 5) / 4);
                ctx.moveTo(x, y);
                ctx.lineTo(x + Math.cos(a) * len * 0.5 + 3, y + Math.sin(a) * len * 0.5);
                ctx.lineTo(x + Math.cos(a + 0.2) * len, y + Math.sin(a + 0.2) * len);
            }
            ctx.stroke();
        }
        if (alive && p.boil > 0) {
            // Bubbles rising, more and faster as it heats.
            const count = Math.round(4 + 14 * p.boil);
            ctx.fillStyle = "rgba(255,255,255,0.55)";
            for (let i = 0; i < count; i++) {
                const seed = (i * 0.618034) % 1;
                const rise = (time * (0.35 + 0.9 * p.boil) + seed * 7.3) % 1;
                const bx = x + (seed * 2 - 1) * inner * 0.75 + Math.sin(time * 3 + i) * 2;
                const by = y + inner - rise * inner * 2;
                circle(ctx, bx, by, 1 + 2.2 * p.boil * (1 - rise * 0.5));
                ctx.fill();
            }
        }
        if (alive && p.burning) {
            ctx.fillStyle = `rgba(255,60,30,${(0.2 + 0.25 * pulse).toFixed(3)})`;
            ctx.fillRect(x - inner, y - inner, inner * 2, inner * 2);
        }
        ctx.restore();

        // The dial round the rim: its dim track is in the background; it is lit up to the shown temperature.
        const arcR = r - 6,
            arcW = 5;
        if (alive && lit > 0) {
            ctx.save();
            ctx.beginPath();
            ctx.moveTo(x, y);
            ctx.arc(x, y, r + 4, DIAL_START, DIAL_START + DIAL_SWEEP * lit);
            ctx.closePath();
            ctx.clip();
            ctx.drawImage(layers.lit, ...layers.sprite);
            ctx.restore();
        }
        if (alive && p.burning) {
            ctx.strokeStyle = `rgba(255,77,51,${(0.35 + 0.5 * pulse).toFixed(3)})`;
            ctx.lineWidth = arcW + 3;
            ctx.beginPath();
            ctx.arc(x, y, arcR, DIAL_START + DIAL_SWEEP * dial.safe, DIAL_START + DIAL_SWEEP);
            ctx.stroke();
        }

        ctx.drawImage(layers.over, ...layers.sprite);

        // The needle at the tip of the lit arc.
        if (alive) {
            const a = DIAL_START + DIAL_SWEEP * lit;
            const dx = Math.cos(a),
                dy = Math.sin(a);
            line(ctx, x, y, dx, dy, arcR - arcW / 2 - 5, arcR + arcW / 2 + 1, "#000", 4.5);
            line(ctx, x, y, dx, dy, arcR - arcW / 2 - 5, arcR + arcW / 2 + 1, "#fff", 2);
        }
    }

    // HeatGauge.DrawTrend: up (red) while digging or firing heats the tank, down (blue) while it cools.
    drawTrend(ctx, p) {
        if (p.dead || Math.abs(this.trend) < 0.02) return;
        const x = TEMP.right - 12,
            y = TEMP.readout,
            s = 6;
        if (this.trend > 0) {
            ctx.fillStyle = abgr(0xff4050ff);
            triangle(ctx, x - s, y + s * 0.6, x + s, y + s * 0.6, x, y - s * 0.8);
        } else {
            ctx.fillStyle = abgr(0xffffc050);
            triangle(ctx, x - s, y - s * 0.6, x + s, y - s * 0.6, x, y + s * 0.8);
        }
    }

    // HeatGauge.DrawOffNetworkTag: above TEMP while outside the network, how long until the heat floor reaches the
    // safe maximum. Under the reserve rule the stage's tag (ReserveTags) sits beside it, to the right.
    drawOffNetwork(ctx, p, time, reserve) {
        if (p.dead || !p.offNetwork) return;
        const text =
            p.safeSeconds > 0
                ? `OFF NETWORK  ~${Math.ceil(p.safeSeconds)}s`
                : "OFF NETWORK  OVERHEATING";
        const blinkOn = Math.floor(time * 4) % 2 === 0;
        const color = p.offNetworkUrgent && blinkOn ? "#ff5642" : "#ffc640";
        const cx = HEAT_X + HEAT_W * 0.5,
            cy = -24;
        const w = this.drawTag(ctx, text, cx, cy, color);
        if (!reserve?.tag) return;
        ctx.font = font(20);
        const tagW = ctx.measureText(reserve.tag).width;
        const tagColor = reserve.blink && !blinkOn ? "#ffc640" : reserve.color;
        this.drawTag(ctx, reserve.tag, cx + w / 2 + 9 + 8 + tagW / 2 + 9, cy, tagColor);
    }

    // HudTheme.DrawTag: text on a dark chamfered plate outlined in its colour. Returns the text's width.
    drawTag(ctx, text, cx, cy, color) {
        ctx.font = font(20);
        const w = ctx.measureText(text).width;
        const x0 = cx - w / 2 - 9,
            y0 = cy - 15,
            x1 = cx + w / 2 + 9,
            y1 = cy + 15;
        chamfer(ctx, x0, y0, x1, y1, 5);
        ctx.fillStyle = "rgba(14,14,16,0.88)";
        ctx.fill();
        ctx.strokeStyle = color + "cc";
        ctx.lineWidth = 1.3;
        ctx.stroke();
        ctx.fillStyle = color;
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(text, cx, cy + 1);
        return w;
    }

    // HudStrip.DrawLedger: SHIELD 400 | 1000 HULL on one line (the hull alone, its number centred, with no shield in
    // the match), a hairline bar under each half.
    drawLedger(ctx, p) {
        const plate = p.plate,
            L = LEDGER,
            hull = p.dead ? 0 : p.hull,
            health = String(p.health),
            hullTrack = plate.glowStops[plate.glowStops.length - 1] + "90",
            hullGlow = abgr(0x30106aff);
        if (!p.shieldActive) {
            stripText(ctx, "HULL", L.left, L.y, L.label, abgr(HULL_NEUTRAL), null, "left");
            stripText(ctx, health, L.split, L.y, L.number, plate.hullColor, hullGlow);
            ledgerBar(ctx, L.left, L.right, hull, false, plate.hullColor, hullTrack);
            return;
        }
        const color = abgr(plate.shield > 0 ? SHIELD_CYAN : SHIELD_SLATE);
        const track = abgr(((0x70 << 24) | (SHIELD_SLATE & 0xffffff)) >>> 0);
        stripText(ctx, "SHIELD", L.left, L.y, L.label, color, null, "left");
        stripText(ctx, String(plate.shield), L.split - SPLIT_GAP, L.y, L.number, color, null, "right");
        drawSplit(ctx, L.split, L.y);
        stripText(ctx, health, L.split + SPLIT_GAP, L.y, L.number, plate.hullColor, hullGlow, "left");
        stripText(ctx, "HULL", L.right, L.y, L.label, abgr(HULL_NEUTRAL), null, "right");
        ledgerBar(ctx, L.left, L.split - L.barGap, p.dead ? 0 : p.shieldFill, false, color, track);
        ledgerBar(ctx, L.split + L.barGap, L.right, hull, true, plate.hullColor, hullTrack);
    }

    // LiquidGauges.Draw: the shield crystal, dark where it has emptied from its edge inward, with the tank's hot
    // centre glowing through the empty part. A low level pulses red and the crystal flashes on a hit.
    drawLiquids(ctx, p, time) {
        const pulse = Math.floor(time * 4) % 2 === 0;
        const crystalLow = p.shieldActive ? p.shieldLow : p.hullLow;
        const crystalFill = p.shieldActive ? (p.dead ? 0 : p.shieldFill) : p.hull;
        const [cx, cy] = centroid(SHIELD_OUTLINE);
        const shrink = Math.sqrt(Math.min(1, Math.max(0, this.shown.crystal)));
        const inner = scaled(SHIELD_OUTLINE, cx, cy, shrink);
        if (p.hitFresh) {
            polygon(ctx, inner);
            ctx.fillStyle = abgr(0x90ffffff);
            ctx.fill();
        } else if (crystalLow && crystalFill > 0 && pulse) {
            polygon(ctx, inner);
            ctx.fillStyle = abgr(0x703030ff);
            ctx.fill();
        }
        // The empty part: the outline less what is left.
        polygon(ctx, SHIELD_OUTLINE);
        ring(ctx, inner);
        ctx.fillStyle = abgr(0xc8080808);
        ctx.fill("evenodd");
        const plate = p.plate;
        if (plate.glow > 0) {
            // The hot centre, from the hull's own heat in the middle to the ramp's dull red at the outline, seen
            // only through the empty part.
            const r = Math.max(...SHIELD_OUTLINE.map(([x, y]) => Math.hypot(x - cx, y - cy)));
            const glow = ctx.createRadialGradient(cx, cy, 0, cx, cy, r);
            const alpha = (a) => Math.round(Math.min(1, Math.max(0, a)) * 255).toString(16).padStart(2, "0");
            // Every stop on the ramp, as the desktop's bands: the colour cools and the glow fades toward the rim.
            const stops = plate.glowStops;
            stops.forEach((colour, i) => {
                const at = i / (stops.length - 1);
                glow.addColorStop(at, colour + alpha(plate.glow * (1 - 0.8 * at)));
            });
            polygon(ctx, SHIELD_OUTLINE);
            ring(ctx, inner);
            ctx.fillStyle = glow;
            ctx.fill("evenodd");
        }
        if (crystalFill > 0) {
            // A bright line along the edge of what is left.
            polygon(ctx, inner);
            ctx.strokeStyle = abgr(0x50ffe0a0);
            ctx.lineWidth = 4;
            ctx.stroke();
            ctx.strokeStyle = abgr(0xffffe0a0);
            ctx.lineWidth = 1.5;
            ctx.stroke();
        }
    }

    // BuildPanel.Draw: a slot per machine, filling toward its cost from the stock that would pay (BuildSlotView).
    drawBuild(ctx, builds, p, time) {
        for (const b of builds) {
            const x0 = BUILD_X + SLOT_X[b.slot % SLOT_X.length],
                y0 = SLOT_Y[Math.floor(b.slot / SLOT_X.length)],
                x1 = x0 + SLOT_W,
                y1 = y0 + SLOT_H;
            if (b.state === "Blocked") {
                ctx.fillStyle = abgr(0xa8000000);
                roundRect(ctx, x0, y0, x1, y1, 6);
                ctx.fill();
            } else if (b.state === "Filling") {
                ctx.fillStyle = abgr(0x80000000);
                roundRect(ctx, x0, y0, x1, y1 - SLOT_H * b.progress, 6);
                ctx.fill();
            }
            ctx.strokeStyle =
                b.state === "Carrying"
                    ? abgr(Math.floor(time * 3) % 2 === 0 ? 0xff40ffff : 0xff20a0a0)
                    : BUILD_STATE_BORDER[b.state];
            ctx.lineWidth = 2.5;
            roundRect(ctx, x0, y0, x1, y1, 6);
            ctx.stroke();
            outlined(ctx, b.label, x0 + 4, y1 - 18, 15, b.state === "Blocked" ? abgr(0xff909090) : "#fff");
            if (b.caption) outlined(ctx, b.caption, x0 + 4, y0 + 3, 13, CAPTION_COLOR[b.captionKind]);
        }
    }
}

// A canvas kept off screen, of this many device pixels, drawn in art pixels: k device pixels to one, moved by
// (dx, dy) device pixels.
function offscreen(width, height, k, dx, dy) {
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, width);
    canvas.height = Math.max(1, height);
    const ctx = canvas.getContext("2d");
    ctx.setTransform(k, 0, 0, k, dx, dy);
    return [canvas, ctx];
}

// HeatGauge's dial round the orb: its bands in their colours, at one alpha ("48" dim, "ff" lit, as hex).
function strokeDial(ctx, dial, alpha) {
    const { x, y, r } = ORB;
    const bands = dial.bands.length;
    ctx.lineWidth = 5;
    for (let i = 0; i < bands; i++) {
        ctx.strokeStyle = dial.bands[i] + alpha;
        ctx.beginPath();
        ctx.arc(x, y, r - 6, DIAL_START + (DIAL_SWEEP * i) / bands, DIAL_START + (DIAL_SWEEP * (i + 1)) / bands + 0.01);
        ctx.stroke();
    }
}

function size(canvas, width, height, density) {
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    canvas.width = Math.ceil(width * density);
    canvas.height = Math.ceil(height * density);
}

// HudStrip.DrawStripText: centred on an art point, sized in art pixels, with a soft glow, a dark outline and a
// second pass a fraction of a pixel over, to match the art's chunky lettering. The glow is a wide stroke in its
// colour, where the desktop draws the text a dozen times around: a blur filter would cost far more.
// `align` is "center", "left" (starting at cx) or "right" (ending at cx).
function stripText(ctx, text, cx, cy, size, color, glow, align = "center") {
    ctx.font = font(size);
    ctx.textAlign = align;
    ctx.textBaseline = "alphabetic";
    const ink = ctx.measureText("0");
    const y = cy + (ink.actualBoundingBoxAscent - ink.actualBoundingBoxDescent) / 2;
    ctx.lineJoin = "round";
    if (glow) {
        ctx.strokeStyle = glow;
        ctx.lineWidth = 7;
        ctx.strokeText(text, cx, y);
        ctx.lineWidth = 4.5;
        ctx.strokeText(text, cx, y);
    }
    ctx.strokeStyle = abgr(0xe0000000);
    ctx.lineWidth = 2.4;
    ctx.strokeText(text, cx, y);
    ctx.strokeText(text, cx + 0.6, y);
    ctx.fillStyle = color;
    ctx.fillText(text, cx, y);
    ctx.fillText(text, cx + 0.6, y);
}

// HudStrip.DrawMaterials: one line, HOLD and the hold's amount left of the split, the store, its rate and BASE right
// of it; the store flashes gold as a deposit lands, and the deposit's +N rises out of it.
function drawMaterials(ctx, m) {
    const L = MATERIALS_LEDGER;
    stripText(ctx, "HOLD", L.left, L.y, L.label, abgr(MATERIALS_LABEL), null, "left");
    stripText(ctx, m.holdText, L.split - SPLIT_GAP, L.y, L.number, abgr(m.holdFull ? 0xff2880ff : 0xffaafacd), null, "right");
    drawSplit(ctx, L.split, L.y);
    const store = lerpAbgr(STORE_COLOR, STORE_FLASH, m.flash);
    const glow = m.flash > 0 ? withAlpha(STORE_FLASH, 0.3 * m.flash) : null;
    stripText(ctx, m.baseText, L.split + SPLIT_GAP, L.y, L.number, abgr(store), glow, "left");
    if (m.rateText) stripText(ctx, m.rateText, L.rateRight, L.y, L.rate, abgr(MATERIALS_RATE), null, "right");
    stripText(ctx, "BASE", L.right, L.y, L.label, abgr(MATERIALS_LABEL), null, "right");
    if (m.deltaText)
        stripText(ctx, m.deltaText, L.split + SPLIT_GAP, L.y - 2 - L.deltaRise * m.deltaRise, L.number,
            withAlpha(STORE_FLASH, m.deltaAlpha), null, "left");
}

// HudStrip.DrawSplit: the hairline between a line's two values.
function drawSplit(ctx, x, y) {
    ctx.fillStyle = abgr(SPLIT_COLOR);
    ctx.fillRect(x - 0.5, y - SPLIT_HALF, 1, 2 * SPLIT_HALF);
}

// HudPalette.Lerp: channel by channel between two ABGR colours.
function lerpAbgr(a, b, t) {
    const k = Math.min(1, Math.max(0, t));
    let out = 0;
    for (let shift = 0; shift < 32; shift += 8) {
        const ca = (a >>> shift) & 0xff, cb = (b >>> shift) & 0xff;
        out |= Math.round(ca + (cb - ca) * k) << shift;
    }
    return out >>> 0;
}

// An ABGR colour with its alpha set to `alpha` (0 to 1), as CSS.
function withAlpha(color, alpha) {
    return abgr(((color & 0x00ffffff) | (Math.round(Math.min(1, Math.max(0, alpha)) * 255) << 24)) >>> 0);
}

// HudStrip.DrawLedgerBar: a hairline bar under the SHIELD/HULL line from `left` to `right`, filled `fill` of the way
// from its left end, or from its right one when `fromRight`.
function ledgerBar(ctx, left, right, fill, fromRight, barColor, trackColor) {
    const barY = LEDGER.y + LEDGER.barDrop,
        half = LEDGER.barHalf,
        edge = LEDGER.barOutline,
        width = right - left;
    ctx.fillStyle = abgr(0xc0000000);
    ctx.fillRect(left - edge, barY - half - edge, width + 2 * edge, 2 * (half + edge));
    ctx.fillStyle = trackColor;
    ctx.fillRect(left, barY - half, width, 2 * half);
    const filled = width * Math.min(1, Math.max(0, fill));
    if (filled > 0) {
        ctx.fillStyle = barColor;
        ctx.fillRect(fromRight ? right - filled : left, barY - half, filled, 2 * half);
    }
}

// HudDraw.DrawOutlinedText: text from its top-left corner, with a one-pixel dark outline.
function outlined(ctx, text, x, y, size, color) {
    ctx.font = font(size);
    ctx.textAlign = "left";
    ctx.textBaseline = "top";
    ctx.fillStyle = abgr(0xe0000000);
    for (const [dx, dy] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) ctx.fillText(text, x + dx, y + dy);
    ctx.fillStyle = color;
    ctx.fillText(text, x, y);
}

function centroid(poly) {
    let x = 0,
        y = 0;
    for (const [px, py] of poly) {
        x += px;
        y += py;
    }
    return [x / poly.length, y / poly.length];
}

// The polygon shrunk toward (cx, cy) by `k`.
function scaled(poly, cx, cy, k) {
    return poly.map(([x, y]) => [cx + (x - cx) * k, cy + (y - cy) * k]);
}

// Adds the polygon to the current path as a second ring (for an even-odd fill), without starting a new path.
function ring(ctx, poly) {
    poly.forEach(([x, y], i) => (i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
    ctx.closePath();
}

function polygon(ctx, poly) {
    ctx.beginPath();
    poly.forEach(([x, y], i) => (i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
    ctx.closePath();
}

function circle(ctx, x, y, r) {
    ctx.beginPath();
    ctx.arc(x, y, Math.max(0, r), 0, Math.PI * 2);
}

function triangle(ctx, ax, ay, bx, by, cx, cy) {
    ctx.beginPath();
    ctx.moveTo(ax, ay);
    ctx.lineTo(bx, by);
    ctx.lineTo(cx, cy);
    ctx.closePath();
    ctx.fill();
}

function line(ctx, x, y, dx, dy, from, to, color, width) {
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.beginPath();
    ctx.moveTo(x + dx * from, y + dy * from);
    ctx.lineTo(x + dx * to, y + dy * to);
    ctx.stroke();
}

function roundRect(ctx, x0, y0, x1, y1, r) {
    ctx.beginPath();
    ctx.roundRect(x0, y0, Math.max(0, x1 - x0), Math.max(0, y1 - y0), r);
}

// HudTheme.ChamferedRect
function chamfer(ctx, x0, y0, x1, y1, c) {
    polygon(ctx, [
        [x0 + c, y0], [x1 - c, y0], [x1, y0 + c], [x1, y1 - c],
        [x1 - c, y1], [x0 + c, y1], [x0, y1 - c], [x0, y0 + c],
    ]);
}

// Two #rrggbb colours mixed, `t` of the way to the second.
function mix(a, b, t) {
    const n = (s, i) => Number.parseInt(s.slice(1 + 2 * i, 3 + 2 * i), 16);
    const c = [0, 1, 2].map((i) => Math.round(n(a, i) + (n(b, i) - n(a, i)) * t));
    return `#${c.map((v) => v.toString(16).padStart(2, "0")).join("")}`;
}
