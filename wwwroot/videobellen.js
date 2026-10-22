// Videobellen op het intranet (Components/Layout/Videobellen.razor, Services/BelCentrale.cs): camera en microfoon gaan
// rechtstreeks van browser naar browser (WebRTC). De app geeft alleen de verbindingsgegevens door (offer, answer, ice).
// Geen STUN/TURN-server: werkt binnen het eigen netwerk, niet van buiten (daarvoor is een relayserver nodig).
// Camera en microfoon geeft de browser alleen vrij op https (of localhost).
(function () {
    'use strict';
    let pc = null, eigen = null, doel = null, wachtrij = [], heeftAnder = false, ring = null, ringCtx = null;

    function el(id) { return document.getElementById(id); }
    function toon() {
        const v = el('mde-video-eigen'); if (v && eigen) { v.srcObject = eigen; v.muted = true; }
    }

    async function media() {
        // eerst met camera; geen camera (of geweigerd) → alleen geluid
        try { return { stroom: await navigator.mediaDevices.getUserMedia({ video: { width: 1280, height: 720 }, audio: true }), video: true }; }
        catch (e) {
            if (e && e.name === 'NotAllowedError') throw new Error('De browser mag de camera en microfoon niet gebruiken. Sta het toe via het slotje links in de adresbalk.');
            try { return { stroom: await navigator.mediaDevices.getUserMedia({ audio: true }), video: false }; }
            catch (e2) {
                if (e2 && e2.name === 'NotAllowedError') throw new Error('De browser mag de microfoon niet gebruiken. Sta het toe via het slotje links in de adresbalk.');
                throw new Error('Geen camera of microfoon gevonden (' + (e2 && e2.message || e2) + ').');
            }
        }
    }

    window.mdeVideo = {
        /// Camera/microfoon aan en de verbinding klaarzetten. Geeft "video", "audio" of gooit een fout met uitleg.
        start: async function (dotnet) {
            if (!window.isSecureContext || !navigator.mediaDevices)
                throw new Error('Videobellen werkt alleen via https (https://everything.mde-automation.nl), niet via http://172.16.1.13.');
            window.mdeVideo.stop();
            doel = dotnet;
            const m = await media();
            eigen = m.stroom;
            toon();
            pc = new RTCPeerConnection({ iceServers: [] });
            eigen.getTracks().forEach(function (t) { pc.addTrack(t, eigen); });
            pc.onicecandidate = function (e) {
                if (e.candidate && doel) doel.invokeMethodAsync('StuurSignaal', 'ice', JSON.stringify(e.candidate)).catch(function () { });
            };
            pc.ontrack = function (e) {
                const v = el('mde-video-ander');
                if (v && e.streams && e.streams[0] && v.srcObject !== e.streams[0]) v.srcObject = e.streams[0];
            };
            pc.onconnectionstatechange = function () {
                if (doel) doel.invokeMethodAsync('Verbinding', pc ? pc.connectionState : 'closed').catch(function () { });
            };
            heeftAnder = false; wachtrij = [];
            return m.video ? 'video' : 'audio';
        },
        /// Beller: na "opgenomen" het aanbod maken.
        aanbod: async function () {
            if (!pc) return;
            const o = await pc.createOffer();
            await pc.setLocalDescription(o);
            await doel.invokeMethodAsync('StuurSignaal', 'offer', JSON.stringify(pc.localDescription));
        },
        /// Signaal van de andere kant.
        ontvang: async function (soort, data) {
            if (!pc) return;
            const d = JSON.parse(data);
            if (soort === 'offer') {
                await pc.setRemoteDescription(d);
                heeftAnder = true;
                const a = await pc.createAnswer();
                await pc.setLocalDescription(a);
                await doel.invokeMethodAsync('StuurSignaal', 'answer', JSON.stringify(pc.localDescription));
            } else if (soort === 'answer') {
                await pc.setRemoteDescription(d);
                heeftAnder = true;
            } else if (soort === 'ice') {
                if (!heeftAnder) { wachtrij.push(d); return; }
            }
            if (heeftAnder) {
                const nog = wachtrij; wachtrij = [];
                for (const c of nog) { try { await pc.addIceCandidate(c); } catch (e) { } }
                if (soort === 'ice') { try { await pc.addIceCandidate(d); } catch (e) { } }
            }
        },
        dempen: function (uit) { if (eigen) eigen.getAudioTracks().forEach(function (t) { t.enabled = !uit; }); },
        camera: function (uit) { if (eigen) eigen.getVideoTracks().forEach(function (t) { t.enabled = !uit; }); },
        /// Video-elementen opnieuw koppelen (na het opnieuw tekenen van het venster).
        koppel: function () {
            toon();
            const v = el('mde-video-ander');
            if (v && pc) { const r = pc.getReceivers().map(function (x) { return x.track; }).filter(Boolean); if (r.length && !v.srcObject) v.srcObject = new MediaStream(r); }
        },
        stop: function () {
            try { if (pc) pc.close(); } catch (e) { }
            pc = null;
            if (eigen) eigen.getTracks().forEach(function (t) { t.stop(); });
            eigen = null; heeftAnder = false; wachtrij = [];
            ['mde-video-eigen', 'mde-video-ander'].forEach(function (id) { const v = el(id); if (v) v.srcObject = null; });
        },
        /// Overgaan: belgeluid (twee tonen, elke 2 s) zolang aan.
        belgeluid: function (aan) {
            if (ring) { clearInterval(ring); ring = null; }
            if (!aan) return;
            try {
                const C = window.AudioContext || window.webkitAudioContext; if (!C) return;
                ringCtx = ringCtx || new C();
                if (ringCtx.state === 'suspended') ringCtx.resume();
                const speel = function () {
                    const t = ringCtx.currentTime;
                    [[660, 0], [880, 0.25], [660, 0.5], [880, 0.75]].forEach(function (p) {
                        const o = ringCtx.createOscillator(), g = ringCtx.createGain();
                        o.type = 'sine'; o.frequency.value = p[0];
                        g.gain.setValueAtTime(0.0001, t + p[1]);
                        g.gain.exponentialRampToValueAtTime(0.2, t + p[1] + 0.02);
                        g.gain.exponentialRampToValueAtTime(0.0001, t + p[1] + 0.22);
                        o.connect(g); g.connect(ringCtx.destination);
                        o.start(t + p[1]); o.stop(t + p[1] + 0.25);
                    });
                };
                speel();
                ring = setInterval(speel, 2000);
            } catch (e) { }
        }
    };
})();
