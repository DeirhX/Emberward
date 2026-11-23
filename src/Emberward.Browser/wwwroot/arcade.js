// A timed match's HUD: the clock with the powerup drops under it, the board, the running powerups, the pointers to
// drops that are off screen, and the final standings. What each says is chosen in C# from the desktop's own types
// (BrowserArcade: Scoreboards, DropBoard); each frame after a step carries it as `arcade`, which is null in a match
// without a clock. This file only shows it, and writes to the page only what changed.

import { setClass, setHidden, setProperty, setText } from "./hud.js";

const $ = (id) => document.getElementById(id);

const HUD_FONT = '"Chakra Petch", system-ui, sans-serif';
// The page's parts a drop pointer keeps clear of.
const PANELS = [
    "#mission", "#toasts", "#arcade-clock", "#scoreboard", "#warning", "#upgrades", "#effects",
    "#hud .tank-status", "#hud .build-status", "#strip-left", "#strip-right",
];
// How many HUD updates pass between two looks at where the panels are.
const MEASURE_EVERY = 12;

function element(tag, className, ...children) {
    const e = document.createElement(tag);
    if (className) e.className = className;
    e.append(...children);
    return e;
}

export class ArcadeView {
    shown = false;
    rows = [];
    drops = [];
    effects = [];
    sinceMeasure = MEASURE_EVERY;
    // Where the drop pointers may go (drawDropPointers), in CSS pixels of the play area.
    layout = { covered: [], gapLeft: 0, gapRight: 0, top: 0, bottom: 0 };

    constructor() {
        this.main = document.querySelector("main");
        this.clock = $("arcade-clock");
        this.board = $("scoreboard");
        // Below this width the clock stands over the board at the right, and nothing has to make room under it.
        this.narrow = matchMedia("(max-width: 700px)");
        // The warning and the network notice go under the clock, and the panels under the board: both need the heights.
        const observer = new ResizeObserver(() => this.makeRoom());
        observer.observe(this.clock);
        observer.observe(this.board);
        this.narrow.addEventListener("change", () => this.makeRoom());
    }

    // `f` is the frame after a step.
    update(f) {
        const a = f.arcade;
        if (!a) {
            if (this.shown) this.clear();
            return;
        }
        this.shown = true;
        document.body.classList.add("arcade");
        this.showClock(a);
        this.showDrops(a.drops);
        this.showRows(a.rows);
        this.showEffects(a.effects);
        this.showOver(a.over, Boolean(f.net));
        setHidden(this.clock, Boolean(a.over));
        setHidden(this.board, false);
        if (++this.sinceMeasure >= MEASURE_EVERY) this.measure();
    }

    clear() {
        this.shown = false;
        document.body.classList.remove("arcade");
        for (const id of ["arcade-clock", "scoreboard", "effects", "arcade-dim", "final", "final-foot"])
            $(id).hidden = true;
        this.board.classList.remove("final");
        this.layout.covered.length = 0;
        this.sinceMeasure = MEASURE_EVERY;
        this.makeRoom();
    }

    // The phase is a class as well as a colour: the last minute frames the plate, the last seconds and sudden death
    // beat (style.css), and the caption says which in words.
    showClock(a) {
        setText($("clock-caption"), a.caption);
        setText($("clock-time"), a.clock);
        setClass(this.clock, `plate phase-${a.phase}${a.urgent ? " urgent" : ""}`);
        setProperty(this.clock, "--clock", a.clockColor);
    }

    // One line per drop: the kind's mark on a chip of its colour, its name, when, and how far or who took it.
    showDrops(drops) {
        const list = $("drop-lines");
        while (this.drops.length > drops.length) this.drops.pop().line.remove();
        while (this.drops.length < drops.length) {
            const chip = element("i", "chip"),
                name = element("span", "name"),
                when = element("span", "when"),
                detail = element("span", "detail");
            const line = element("li", "", chip, name, when, detail);
            list.append(line);
            this.drops.push({ line, chip, name, when, detail });
        }
        drops.forEach((drop, i) => {
            const row = this.drops[i];
            setClass(row.line, `drop-${drop.state}${drop.urgent ? " urgent" : ""}${drop.pointed ? "" : " past"}`);
            setProperty(row.line, "--kind", drop.color);
            setText(row.chip, drop.mark);
            setText(row.name, drop.name);
            setText(row.when, drop.when);
            setText(row.detail, drop.detail);
        });
        setHidden(list, drops.length === 0);
    }

    // The board: place, a mark for the tank that leads alone, the tank's colour, its name, kills and deaths. The
    // player's own line has a band and a bar at its left.
    showRows(rows) {
        const list = $("score-rows");
        while (this.rows.length > rows.length) this.rows.pop().line.remove();
        while (this.rows.length < rows.length) {
            const rank = element("span", "rank"),
                lead = element("span", "lead"),
                swatch = element("i", "swatch"),
                name = element("span", "name"),
                kills = element("span", "kills"),
                deaths = element("span", "deaths");
            lead.setAttribute("aria-hidden", "true");
            const line = element("li", "", rank, lead, swatch, name, kills, deaths);
            list.append(line);
            this.rows.push({ line, rank, lead, name, kills, deaths });
        }
        rows.forEach((r, i) => {
            const row = this.rows[i];
            setClass(row.line, `${r.local ? "local" : ""}${r.leader ? " leader" : ""}`);
            setProperty(row.line, "--tank", r.color);
            setText(row.rank, String(r.rank));
            setText(row.lead, r.leader ? "▲" : "");
            setText(row.name, r.name);
            setText(row.kills, String(r.kills));
            setText(row.deaths, String(r.deaths));
            const label =
                `Place ${r.rank}: ${r.name}, ${r.kills} kills, ${r.deaths} deaths` +
                (r.local ? ", you" : "") + (r.leader ? ", leading" : "");
            if (row.line.getAttribute("aria-label") !== label) row.line.setAttribute("aria-label", label);
        });
    }

    // The running powerups, side by side over the bottom of the view: mark, name and seconds left, and a bar that
    // drains. One in its last seconds blinks.
    showEffects(effects) {
        const list = $("effects");
        while (this.effects.length > effects.length) this.effects.pop().tag.remove();
        while (this.effects.length < effects.length) {
            const text = element("span"),
                bar = element("i");
            const tag = element("div", "", text, bar);
            list.append(tag);
            this.effects.push({ tag, text, bar });
        }
        effects.forEach((effect, i) => {
            const row = this.effects[i];
            setClass(row.tag, `effect${effect.blink ? " blink" : ""}`);
            setProperty(row.tag, "--kind", effect.color);
            setText(row.text, effect.text);
            // In steps of 1/200, finer than the bar's pixels, so a bar that has not moved writes nothing.
            setProperty(row.bar, "--left", String(Math.round(Math.min(1, Math.max(0, effect.fraction)) * 200) / 200));
        });
        setHidden(list, effects.length === 0);
    }

    // A match the score decided: the board moves to the middle with the result over it. The world behind stands
    // still or plays on; the board is what there is to read.
    showOver(over, network) {
        const final = Boolean(over);
        if (this.board.classList.contains("final") !== final) {
            this.board.classList.toggle("final", final);
            this.sinceMeasure = MEASURE_EVERY;
        }
        setHidden($("final"), !final);
        setHidden($("final-foot"), !final);
        setHidden($("arcade-dim"), !final);
        if (!final) return;
        setText($("final-title"), over.title);
        setClass($("final-title"), over.won ? "tone-Good" : "tone-Bad");
        setText($("final-reason"), over.reason);
        setText($("final-own"), over.own ?? "");
        setText($("final-foot"), network ? "Open the menu to leave the match." : "Open the menu for a new world.");
    }

    // The heights the style sheet places other things by: what stands under the clock at the top of the view
    // (--arcade-top) and under the board at its right (--arcade-board).
    makeRoom() {
        const style = document.body.style;
        const clock = this.shown && !this.narrow.matches ? this.clock.offsetHeight : 0;
        const board = this.shown && !this.board.classList.contains("final") ? this.board.offsetHeight : 0;
        const stack = this.shown && this.narrow.matches ? this.clock.offsetHeight : 0;
        const values = {
            "--arcade-top": `${clock ? clock + 8 : 0}px`,
            "--arcade-board": `${board ? board + 8 : 0}px`,
            "--arcade-stack": `${stack ? stack + 6 : 0}px`,
        };
        for (const [name, value] of Object.entries(values))
            if (style.getPropertyValue(name) !== value) style.setProperty(name, value);
        this.sinceMeasure = MEASURE_EVERY;
    }

    // Where the panels are now, for the drop pointers. A look costs the browser a layout, so it is taken twice a
    // second and when a panel changed size, not with every update.
    measure() {
        this.sinceMeasure = 0;
        const origin = this.main.getBoundingClientRect();
        const { layout } = this;
        layout.covered.length = 0;
        layout.gapLeft = 0;
        layout.gapRight = origin.width;
        layout.top = 0;
        layout.bottom = origin.height;
        for (const selector of PANELS) {
            const r = document.querySelector(selector).getBoundingClientRect();
            if (r.width < 1 || r.height < 1) continue;
            const box = { left: r.left - origin.left, top: r.top - origin.top, right: r.right - origin.left, bottom: r.bottom - origin.top };
            if (selector === "#effects") {
                layout.bottom = box.top;
                continue;
            }
            layout.covered.push(box);
            // The gap between what stands at the left of the view's top and what stands at its right.
            if (selector === "#mission" || selector === "#toasts") layout.gapLeft = Math.max(layout.gapLeft, box.right);
            if (selector === "#scoreboard") layout.gapRight = Math.min(layout.gapRight, box.left);
            if (selector === "#arcade-clock" && !this.narrow.matches) layout.top = box.bottom;
        }
    }
}

const SEEN = 28,
    MARGIN = 62,
    RADIUS = 15,
    BANNER_ROOM = 34,
    DARK = "#0e0e10";

function isUnder(covered, x, y, pad) {
    for (const box of covered)
        if (x > box.left - pad && x < box.right + pad && y > box.top - pad && y < box.bottom + pad) return true;
    return false;
}

function roundedBox(c, x, y, w, h, r) {
    c.beginPath();
    if (c.roundRect) c.roundRect(x, y, w, h, r);
    else c.rect(x, y, w, h);
}

// While a drop's site is off screen, or under one of the page's panels, a round badge at the edge of the view with
// the kind's mark on its colour, a beak toward the site, and a tag saying which, when and how far (`pointerText`), as
// the desktop's GameHud.DrawDropPointer draws it. A drop still to land has a broken ring that turns; one about to
// land or to run out beats. `view` is the renderer: its size, and where a cell is from the centre of the view.
// Returns whether anything was drawn.
export function drawDropPointers(c, view, drops, layout, now) {
    const badges = [],
        tags = [];
    let drawn = false;
    for (const drop of drops)
        if (drop.pointed) drawn = drawDropPointer(c, view, drop, layout, now / 1000, badges, tags) || drawn;
    return drawn;
}

function drawDropPointer(c, view, drop, layout, time, badges, tags) {
    const { dx, dy } = view.fromCentre(drop.site);
    const sx = view.width / 2 + dx,
        sy = view.height / 2 + dy;
    const bottom = Math.min(view.height, layout.bottom || view.height);
    // Below the clock, and below where a warning goes under it.
    const top = layout.top + BANNER_ROOM;
    if (
        sx >= SEEN && sx <= view.width - SEEN && sy >= SEEN && sy <= bottom - SEEN &&
        !isUnder(layout.covered, sx, sy, SEEN)
    )
        return false;
    const cx = view.width / 2,
        cy = bottom / 2;
    let ux = sx - cx,
        uy = sy - cy;
    const length = Math.hypot(ux, uy);
    if (length < 1) return false;
    ux /= length;
    uy /= length;
    const halfW = view.width / 2 - MARGIN,
        halfDown = bottom - cy - MARGIN,
        halfUp = Math.max(cy - top - (RADIUS + 15), RADIUS);
    let t = Math.min(
        halfW / Math.max(Math.abs(ux), 1e-3),
        (uy < 0 ? halfUp : halfDown) / Math.max(Math.abs(uy), 1e-3),
    );
    // A site under a panel is nearer than the edge: stop short of it, so that the beak still points at it.
    t = Math.min(t, Math.max(length - (RADIUS + 30), 0));
    let x = cx + ux * t,
        y = cy + uy * t;
    // Not on a panel: into the gap between the card and the board, still pointing the same way.
    const clear = RADIUS + 22;
    if (isUnder(layout.covered, x, y, clear)) {
        const lo = layout.gapLeft + clear,
            hi = (layout.gapRight || view.width) - clear;
        x = lo <= hi ? Math.min(Math.max(x, lo), hi) : (lo + hi) / 2;
    }
    // Two drops the same way: the later badge goes beside the earlier one.
    const apart = RADIUS * 2 + 14;
    for (const other of badges) {
        const bx = x - other.x,
            by = y - other.y;
        if (bx * bx + by * by >= apart * apart) continue;
        const along = bx * -uy + by * ux >= 0 ? apart : -apart;
        x = other.x - uy * along;
        y = other.y + ux * along;
    }
    badges.push({ x, y });

    const pulse = drop.urgent ? 0.5 + 0.5 * Math.sin(time * 9) : 1;
    c.save();
    c.globalAlpha = 0.65 + 0.35 * pulse;
    c.fillStyle = c.strokeStyle = drop.color;
    c.beginPath();
    c.moveTo(x + ux * (RADIUS + 13), y + uy * (RADIUS + 13));
    c.lineTo(x + ux * (RADIUS - 2) - uy * 9, y + uy * (RADIUS - 2) + ux * 9);
    c.lineTo(x + ux * (RADIUS - 2) + uy * 9, y + uy * (RADIUS - 2) - ux * 9);
    c.closePath();
    c.fill();
    c.globalAlpha = 0.92;
    c.fillStyle = DARK;
    c.beginPath();
    c.arc(x, y, RADIUS, 0, Math.PI * 2);
    c.fill();
    c.globalAlpha = 0.65 + 0.35 * pulse;
    c.lineWidth = 2.6;
    c.beginPath();
    c.arc(x, y, RADIUS, 0, Math.PI * 2);
    c.stroke();
    if (drop.state === "Forecast") {
        c.lineWidth = 1.6;
        for (let i = 0; i < 4; i++) {
            const from = (i * Math.PI) / 2 + time;
            c.beginPath();
            c.arc(x, y, RADIUS + 5, from, from + 0.9);
            c.stroke();
        }
    }
    // The kind's mark, dark on a chip of the kind's colour: the kind is never told by the colour alone.
    c.globalAlpha = 1;
    c.fillStyle = drop.color;
    roundedBox(c, x - 9, y - 9, 18, 18, 3);
    c.fill();
    c.fillStyle = DARK;
    c.font = `700 15px ${HUD_FONT}`;
    c.textAlign = "center";
    c.textBaseline = "middle";
    c.fillText(drop.mark, x, y + 1);

    // The tag toward the middle of the view, clear of the badge whichever way the beak points.
    c.font = `14px ${HUD_FONT}`;
    const halfText = c.measureText(drop.pointerText).width / 2,
        halfLine = 7;
    const reach = RADIUS + 12 + Math.abs(ux) * (halfText + 9) + Math.abs(uy) * (halfLine + 5);
    const half = { x: halfText + 11, y: halfLine + 7 };
    let tx = Math.min(Math.max(x - ux * reach, half.x + 4), view.width - half.x - 4),
        ty = y - uy * reach;
    // Two tags the same way: the later one goes under the earlier one. Each move puts it wholly below one tag.
    for (let pass = 0; pass <= tags.length; pass++) {
        let moved = false;
        for (const tag of tags)
            if (tx + half.x > tag.left && tx - half.x < tag.right && ty + half.y > tag.top && ty - half.y < tag.bottom) {
                ty = tag.bottom + half.y + 1;
                moved = true;
            }
        if (!moved) break;
    }
    tags.push({ left: tx - half.x, top: ty - half.y, right: tx + half.x, bottom: ty + half.y });
    c.globalAlpha = 0.9;
    c.fillStyle = DARK;
    roundedBox(c, tx - half.x, ty - half.y, half.x * 2, half.y * 2, 4);
    c.fill();
    c.globalAlpha = 0.85;
    c.lineWidth = 1.2;
    c.stroke();
    c.globalAlpha = 1;
    c.fillStyle = drop.color;
    c.fillText(drop.pointerText, tx, ty + 1);
    c.restore();
    return true;
}
