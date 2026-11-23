// The scoreboard shown while Tab is held: every tank in its colour, with its driver in a network match, how many times
// it died and when. What it says is chosen in .NET (TankDeathView, shared with the desktop; BrowserFrameBuilder).
// It writes to the page only while it shows, and only when what it shows changed.

const $ = (id) => document.getElementById(id);

export class ScoreboardView {
    shown = false;
    drawn = "";

    show(shown) {
        this.shown = shown;
        $("scoreboard").hidden = !shown;
        if (!shown) this.drawn = "";
    }

    update(board, net) {
        if (!this.shown || !board) return;
        const names = net?.started ? net.roster : null;
        const key = JSON.stringify([board, names?.map((s) => s.name)]);
        if (key === this.drawn) return;
        this.drawn = key;
        $("scoreboard-clock").textContent = board.clock;
        $("scoreboard-rows").replaceChildren(
            ...board.rows.map((row) => {
                const item = document.createElement("li");
                const swatch = document.createElement("i");
                swatch.style.background = row.color;
                const name = document.createElement("span");
                name.className = "name";
                const driver = names?.[row.tank]?.name;
                name.textContent =
                    (driver ? `${row.name}  ${driver}` : row.name) +
                    (row.tank === board.you ? " (you)" : "");
                const count = document.createElement("b");
                count.textContent = String(row.deaths.length);
                const times = document.createElement("span");
                times.className = "times";
                times.textContent = row.deaths.length
                    ? row.deaths.join("    ")
                    : "no deaths";
                item.append(swatch, name, count, times);
                item.classList.toggle("you", row.tank === board.you);
                item.classList.toggle("none", row.deaths.length === 0);
                return item;
            }),
        );
    }
}
