// 3D-viewer voor STEP/STL — three.js + occt-import-js (OpenCascade in WebAssembly).
// Libraries laden pas op het moment dat de viewerpagina geopend wordt (CDN).
window.viewer3d = (function () {
    "use strict";

    const THREE_URL = "https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js";
    const ORBIT_URL = "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/controls/OrbitControls.js";
    const STL_URL = "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/loaders/STLLoader.js";
    const OCCT_URL = "https://cdn.jsdelivr.net/npm/occt-import-js/dist/occt-import-js.min.js";

    let scene, camera, renderer, controls, model;

    function status(tekst) {
        const el = document.getElementById("viewer3d-status");
        if (el) el.textContent = tekst;
    }

    function laadScript(src) {
        return new Promise((res, rej) => {
            if (document.querySelector('script[src="' + src + '"]')) return res();
            const s = document.createElement("script");
            s.src = src;
            s.onload = res;
            s.onerror = () => rej(new Error("kon niet laden: " + src));
            document.head.appendChild(s);
        });
    }

    function materiaal(kleur) {
        return new THREE.MeshStandardMaterial({
            color: kleur || 0x5b7fa6, metalness: 0.35, roughness: 0.55
        });
    }

    // Demo-onderdeel: montageplaat met assteunen (procedureel, geen bestand nodig)
    function demoModel() {
        const g = new THREE.Group();
        const plaat = new THREE.Mesh(new THREE.BoxGeometry(120, 8, 80), materiaal(0x6d87a8));
        g.add(plaat);
        for (const [x, z] of [[-48, -28], [48, -28], [-48, 28], [48, 28]]) {
            const boutkop = new THREE.Mesh(new THREE.CylinderGeometry(5, 5, 6, 24), materiaal(0x3f5878));
            boutkop.position.set(x, 7, z);
            g.add(boutkop);
        }
        const steunL = new THREE.Mesh(new THREE.BoxGeometry(14, 42, 60), materiaal(0x6d87a8));
        steunL.position.set(-30, 25, 0);
        g.add(steunL);
        const steunR = steunL.clone(); steunR.position.x = 30;
        g.add(steunR);
        const as = new THREE.Mesh(new THREE.CylinderGeometry(9, 9, 100, 32), materiaal(0x9aa7b5));
        as.rotation.z = Math.PI / 2;
        as.position.y = 38;
        g.add(as);
        const lagerL = new THREE.Mesh(new THREE.CylinderGeometry(15, 15, 16, 32), materiaal(0x40536b));
        lagerL.rotation.z = Math.PI / 2;
        lagerL.position.set(-30, 38, 0);
        g.add(lagerL);
        const lagerR = lagerL.clone(); lagerR.position.x = 30;
        g.add(lagerR);
        return g;
    }

    function toon(obj) {
        if (model) scene.remove(model);
        model = obj;
        // centreren en passend zoomen
        const box = new THREE.Box3().setFromObject(obj);
        const centrum = box.getCenter(new THREE.Vector3());
        obj.position.sub(centrum);
        scene.add(obj);
        const maat = box.getSize(new THREE.Vector3()).length();
        camera.position.set(maat * 0.8, maat * 0.6, maat * 0.9);
        camera.near = maat / 1000; camera.far = maat * 10;
        camera.updateProjectionMatrix();
        controls.target.set(0, 0, 0);
        controls.update();
    }

    async function openBestand(ev) {
        const f = ev.target.files[0];
        if (!f) return;
        await laadBuffer(await f.arrayBuffer(), f.name);
    }

    // Zelfde tekening, maar opgehaald van de server — gebruikt om een .stp uit de
    // bijlagen van een orderregel te tonen zonder hem eerst te downloaden.
    async function openUrl(url, naam) {
        status("Bestand ophalen…");
        try {
            const antwoord = await fetch(url);
            if (!antwoord.ok) { status("Bestand kon niet worden opgehaald."); return; }
            await laadBuffer(await antwoord.arrayBuffer(), naam || url);
        } catch (e) {
            status("Bestand kon niet worden opgehaald (" + e.message + ").");
        }
    }

    async function laadBuffer(buf, bestandsnaam) {
        const f = { name: bestandsnaam };
        const naam = (bestandsnaam || "").toLowerCase();

        if (naam.endsWith(".stl")) {
            try {
                const geom = new THREE.STLLoader().parse(buf);
                geom.computeVertexNormals();
                toon(new THREE.Mesh(geom, materiaal()));
                status(f.name);
            } catch (e) { status("STL kon niet gelezen worden."); }
            return;
        }

        if (naam.endsWith(".step") || naam.endsWith(".stp")) {
            status("STEP laden… (eerste keer downloadt de browser de CAD-kernel, ±10 MB)");
            try {
                await laadScript(OCCT_URL);
                const occt = await occtimportjs();
                const res = occt.ReadStepFile(new Uint8Array(buf), null);
                if (!res || !res.success) throw new Error("parsefout");
                const groep = new THREE.Group();
                for (const m of res.meshes) {
                    const geom = new THREE.BufferGeometry();
                    geom.setAttribute("position", new THREE.Float32BufferAttribute(m.attributes.position.array, 3));
                    if (m.attributes.normal)
                        geom.setAttribute("normal", new THREE.Float32BufferAttribute(m.attributes.normal.array, 3));
                    else geom.computeVertexNormals();
                    geom.setIndex(m.index.array);
                    let kleur;
                    if (m.color) kleur = new THREE.Color(m.color[0], m.color[1], m.color[2]);
                    groep.add(new THREE.Mesh(geom, materiaal(kleur)));
                }
                toon(groep);
                status(f.name + " — " + res.meshes.length + " onderdelen");
            } catch (e) {
                status("STEP kon niet geladen worden (" + e.message + "). In productie: server-side voorconversie naar glTF.");
            }
            return;
        }
        status("Bestandstype niet ondersteund. Kies .stl, .step of .stp.");
    }

    // elementId is optioneel: zo kan dezelfde viewer ook in het bijlagenvenster
    async function init(elementId) {
        const el = document.getElementById(elementId || "viewer3d");
        if (!el) return;
        status("3D-omgeving laden…");
        try {
            await laadScript(THREE_URL);
            await laadScript(ORBIT_URL);
            await laadScript(STL_URL);
        } catch (e) {
            status("three.js kon niet geladen worden — is er internetverbinding? (" + e.message + ")");
            return;
        }

        scene = new THREE.Scene();
        scene.background = new THREE.Color(0xf4f6f9);
        camera = new THREE.PerspectiveCamera(45, el.clientWidth / el.clientHeight, 0.1, 5000);
        renderer = new THREE.WebGLRenderer({ antialias: true });
        renderer.setPixelRatio(window.devicePixelRatio);
        renderer.setSize(el.clientWidth, el.clientHeight);
        el.innerHTML = "";
        el.appendChild(renderer.domElement);

        scene.add(new THREE.AmbientLight(0xffffff, 0.55));
        const licht1 = new THREE.DirectionalLight(0xffffff, 0.7);
        licht1.position.set(1, 2, 1.5);
        scene.add(licht1);
        const licht2 = new THREE.DirectionalLight(0xffffff, 0.25);
        licht2.position.set(-1.5, -0.5, -1);
        scene.add(licht2);
        const grid = new THREE.GridHelper(300, 30, 0xbbc4cf, 0xe2e7ee);
        grid.position.y = -40;
        scene.add(grid);

        controls = new THREE.OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;

        // Alleen op de demopagina een voorbeeldmodel; in het bijlagenvenster
        // komt er meteen een echt bestand in.
        const input = document.getElementById("viewer3d-file");
        if (input) {
            toon(demoModel());
            status("Demo-onderdeel geladen — slepen = draaien, scrollen = zoomen. Open een eigen .stl / .step bestand.");
            input.addEventListener("change", openBestand);
        } else {
            status("");
        }

        new ResizeObserver(() => {
            if (!el.clientWidth) return;
            camera.aspect = el.clientWidth / el.clientHeight;
            camera.updateProjectionMatrix();
            renderer.setSize(el.clientWidth, el.clientHeight);
        }).observe(el);

        (function animatie() {
            requestAnimationFrame(animatie);
            controls.update();
            renderer.render(scene, camera);
        })();
    }

    return { init: init, openUrl: openUrl };
})();
