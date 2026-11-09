// Organigram van de BV-structuur (Financiën > BV-structuur, 9-11-2026): lijnen tussen de kaarten met het percentage
// erop. De kaarten zelf zet Blazor neer (in lagen, data-bv="id"); dit script meet ze en tekent er een SVG achter.
// Haakse lijnen: van de onderkant van de aandeelhouder omlaag, horizontaal tot boven de BV, en dan de BV in. Heeft een
// BV meer aandeelhouders, dan komen ze op verschillende plekken binnen (van links naar rechts zoals ze staan).
window.mdeOrganigram = (function () {
    const NS = "http://www.w3.org/2000/svg";
    const staat = new WeakMap();   // vak -> { randen, observer }

    function el(naam, attrs) {
        const e = document.createElementNS(NS, naam);
        for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
        return e;
    }

    function teken(vak) {
        const s = staat.get(vak);
        if (!s || !vak.isConnected) return;
        let svg = vak.querySelector(":scope > svg.org-lijnen");
        if (!svg) {
            svg = el("svg", { class: "org-lijnen", "aria-hidden": "true" });
            vak.prepend(svg);
        }
        const r0 = vak.getBoundingClientRect();
        svg.setAttribute("width", vak.scrollWidth);
        svg.setAttribute("height", vak.scrollHeight);
        svg.replaceChildren();

        const kaart = id => vak.querySelector(`[data-bv="${id}"]`);
        const perBv = new Map();
        for (const r of s.randen) {
            if (!perBv.has(r.naar)) perBv.set(r.naar, []);
            perBv.get(r.naar).push(r);
        }
        const labels = [];
        for (const [naar, lijst] of perBv) {
            const k = kaart(naar);
            if (!k) continue;
            const kr = k.getBoundingClientRect();
            const items = lijst.map(r => ({ r, p: kaart(r.van) }))
                .filter(x => x.p)
                .map(x => ({ ...x, pr: x.p.getBoundingClientRect() }))
                .sort((a, b) => (a.pr.left + a.pr.width / 2) - (b.pr.left + b.pr.width / 2));
            items.forEach((it, i) => {
                const px = it.pr.left + it.pr.width / 2 - r0.left;
                const py = it.pr.bottom - r0.top;
                const kx = kr.left + kr.width * (i + 1) / (items.length + 1) - r0.left;
                const ky = kr.top - r0.top;
                const my = Math.max(py + 6, ky - 26);
                svg.append(el("path", { class: "org-lijn", d: `M${px},${py} V${my} H${kx} V${ky}` }));
                labels.push({ x: kx, y: (my + ky) / 2, tekst: it.r.pct });
            });
        }
        // percentages bovenop de lijnen
        for (const l of labels) {
            const t = el("text", { class: "org-pct", x: l.x, y: l.y, "text-anchor": "middle", "dominant-baseline": "central" });
            t.textContent = l.tekst;
            svg.append(t);
            const b = t.getBBox();
            svg.insertBefore(el("rect", { class: "org-pct-vak", x: b.x - 4, y: b.y - 1, width: b.width + 8, height: b.height + 2, rx: 7 }), t);
        }
    }

    return {
        /** Tekent de lijnen; randen = [{ van, naar, pct }]. Tekent opnieuw bij elke maatverandering van het vak. */
        teken(vak, randen) {
            if (!vak) return;
            let s = staat.get(vak);
            if (!s) {
                s = { randen: [] };
                s.observer = new ResizeObserver(() => teken(vak));
                s.observer.observe(vak);
                staat.set(vak, s);
            }
            s.randen = randen || [];
            requestAnimationFrame(() => teken(vak));
        },
        stop(vak) {
            const s = vak && staat.get(vak);
            if (s) { s.observer.disconnect(); staat.delete(vak); }
        },
    };
})();
