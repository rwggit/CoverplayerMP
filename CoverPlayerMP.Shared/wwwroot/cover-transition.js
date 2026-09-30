// Portierung der Überblendungen aus Hilfsklassen.AnimationsPanel / AnimationBild auf ein Canvas.
window.coverTransition = (() => {
    const states = new WeakMap();
    let aniIndex = -1;
    let subdivisions = 1;

    const rnd = (a, b) => a + Math.random() * (b - a);
    const clamp = v => v < 0 ? 0 : v > 1 ? 1 : v;

    // Originalkoordinaten aus AnimationBild.AnimationSchmetterling (relativ zu Breite/Höhe).
    const butterflyShape = (() => {
        const k = [
            0.058, 0.000, 0.078, 0.000, 0.096, 0.003, 0.115, 0.005, 0.132, 0.010, 0.149, 0.015, 0.166, 0.020,
            0.181, 0.027, 0.196, 0.035, 0.210, 0.044, 0.224, 0.052, 0.238, 0.062, 0.252, 0.071, 0.265, 0.081,
            0.277, 0.092, 0.291, 0.103, 0.303, 0.114, 0.315, 0.126, 0.327, 0.138, 0.339, 0.150, 0.349, 0.163,
            0.361, 0.175, 0.372, 0.188, 0.383, 0.202, 0.393, 0.217, 0.403, 0.230, 0.414, 0.244, 0.424, 0.259,
            0.433, 0.274, 0.444, 0.287, 0.454, 0.303, 0.463, 0.318, 0.473, 0.333, 0.477, 0.309, 0.483, 0.289,
            0.491, 0.272, 0.504, 0.266, 0.514, 0.281, 0.521, 0.299, 0.526, 0.321, 0.533, 0.326, 0.541, 0.309,
            0.551, 0.294, 0.561, 0.279, 0.571, 0.266, 0.581, 0.250, 0.592, 0.237, 0.601, 0.222, 0.612, 0.208,
            0.623, 0.195, 0.634, 0.182, 0.646, 0.170, 0.657, 0.156, 0.669, 0.145, 0.679, 0.131, 0.691, 0.119,
            0.703, 0.108, 0.717, 0.097, 0.729, 0.086, 0.742, 0.076, 0.755, 0.066, 0.770, 0.057, 0.783, 0.047,
            0.797, 0.039, 0.813, 0.032, 0.827, 0.024, 0.844, 0.018, 0.860, 0.012, 0.876, 0.007, 0.894, 0.003,
            0.914, 0.002, 0.934, 0.002, 0.953, 0.003, 0.971, 0.007, 0.984, 0.017, 0.993, 0.034, 0.998, 0.055,
            0.999, 0.082, 0.998, 0.106, 0.995, 0.131, 0.992, 0.155, 0.988, 0.178, 0.983, 0.200, 0.980, 0.224,
            0.976, 0.247, 0.971, 0.269, 0.968, 0.292, 0.963, 0.314, 0.958, 0.336, 0.953, 0.358, 0.948, 0.380,
            0.942, 0.400, 0.936, 0.420, 0.932, 0.442, 0.924, 0.461, 0.918, 0.481, 0.911, 0.499, 0.903, 0.516,
            0.894, 0.533, 0.882, 0.545, 0.868, 0.553, 0.850, 0.556, 0.833, 0.561, 0.837, 0.575, 0.846, 0.590,
            0.855, 0.607, 0.862, 0.625, 0.869, 0.644, 0.874, 0.666, 0.878, 0.689, 0.879, 0.716, 0.876, 0.741,
            0.872, 0.763, 0.867, 0.785, 0.861, 0.805, 0.854, 0.824, 0.846, 0.842, 0.838, 0.859, 0.830, 0.876,
            0.821, 0.892, 0.812, 0.908, 0.802, 0.923, 0.791, 0.936, 0.780, 0.950, 0.770, 0.963, 0.756, 0.973,
            0.743, 0.983, 0.729, 0.992, 0.712, 0.997, 0.694, 1.000, 0.676, 0.997, 0.661, 0.988, 0.648, 0.978,
            0.635, 0.968, 0.624, 0.955, 0.615, 0.939, 0.605, 0.924, 0.597, 0.908, 0.587, 0.892, 0.580, 0.874,
            0.571, 0.857, 0.564, 0.839, 0.557, 0.820, 0.549, 0.803, 0.541, 0.785, 0.534, 0.766, 0.531, 0.780,
            0.526, 0.802, 0.521, 0.824, 0.515, 0.844, 0.507, 0.861, 0.491, 0.861, 0.483, 0.844, 0.477, 0.824,
            0.473, 0.800, 0.469, 0.776, 0.465, 0.768, 0.456, 0.785, 0.449, 0.803, 0.442, 0.822, 0.433, 0.839,
            0.426, 0.857, 0.419, 0.876, 0.411, 0.892, 0.402, 0.909, 0.393, 0.924, 0.383, 0.939, 0.373, 0.955,
            0.361, 0.966, 0.349, 0.978, 0.336, 0.988, 0.321, 0.995, 0.303, 0.998, 0.283, 0.997, 0.268, 0.990,
            0.253, 0.982, 0.240, 0.971, 0.227, 0.961, 0.216, 0.948, 0.205, 0.934, 0.196, 0.919, 0.185, 0.906,
            0.176, 0.889, 0.167, 0.874, 0.158, 0.857, 0.151, 0.839, 0.144, 0.820, 0.137, 0.802, 0.131, 0.782,
            0.126, 0.760, 0.122, 0.736, 0.120, 0.714, 0.121, 0.687, 0.125, 0.664, 0.130, 0.642, 0.137, 0.624,
            0.144, 0.605, 0.152, 0.588, 0.162, 0.573, 0.164, 0.561, 0.148, 0.556, 0.131, 0.551, 0.115, 0.545,
            0.104, 0.531, 0.095, 0.516, 0.088, 0.497, 0.080, 0.479, 0.073, 0.461, 0.067, 0.440, 0.061, 0.420,
            0.056, 0.398, 0.050, 0.378, 0.046, 0.356, 0.041, 0.334, 0.036, 0.313, 0.032, 0.289, 0.028, 0.267,
            0.023, 0.245, 0.019, 0.222, 0.016, 0.198, 0.011, 0.176, 0.008, 0.151, 0.005, 0.128, 0.001, 0.104,
            0.000, 0.077, 0.002, 0.055, 0.007, 0.034, 0.016, 0.017, 0.029, 0.007, 0.046, 0.002
        ];
        const points = [];
        for (let i = 0; i < k.length; i += 2)
            points.push([k[i], k[i + 1]]);
        return points;
    })();

    function rotate(pts, cx, cy, alpha) {
        const s = Math.sin(alpha), c = Math.cos(alpha);
        for (const p of pts) {
            const x = p.x - cx, y = p.y - cy;
            p.x = c * x - s * y + cx;
            p.y = s * x + c * y + cy;
        }
    }

    function tran(pts, mx, my, alpha, factor, px, py) {
        const s = Math.sin(alpha), c = Math.cos(alpha);
        for (const p of pts) {
            const x = (p.x - mx) * factor, y = (p.y - my) * factor;
            p.x = c * x - s * y + px;
            p.y = s * x + c * y + py;
        }
    }

    class Pic {
        constructor(img, fw, fh) {
            this.img = img;
            this.fw = fw;
            this.fh = fh;
            const scale = Math.min(fw / img.width, fh / img.height);
            this.W = Math.max(1, img.width * scale);
            this.H = Math.max(1, img.height * scale);
            this.baseX = (fw - this.W) / 2;
            this.baseY = (fh - this.H) / 2;
            this.full();
        }

        full() {
            this.pos = { x: this.baseX, y: this.baseY };
            this.pts = [{ x: 0, y: 0 }, { x: this.W, y: 0 }, { x: 0, y: this.H }];
            this.poly = null;
            this.clock = null;
            this.visible = true;
        }

        hide() { this.visible = false; }

        outRight(t) { this.full(); const m = this.W * t; this.pts[0].x = m; this.pts[2].x = m; }
        inLeft(t) { this.full(); this.pts[1].x = this.W * t; }
        outTopDown(t) { this.full(); const m = this.H * t; this.pts[0].y = m; this.pts[1].y = m; }
        inTopDown(t) { this.full(); this.pts[2].y = this.H * t; }

        turnOut(t) {
            this.full();
            if (t < .5) this.pts[1].y = 2 * this.H * t;
            else this.pts[1] = { x: this.W - 2 * this.W * (t - .5), y: this.H };
        }

        turnIn(t) {
            this.full();
            if (t < .5) this.pts[2] = { x: this.W, y: 2 * this.H * t };
            else this.pts[2] = { x: this.W - 2 * this.W * (t - .5), y: this.H };
        }

        zoomOut(t) {
            this.full();
            const f = t / 2, W = this.W, H = this.H;
            this.pts = [{ x: W * f, y: H * f }, { x: W - W * f, y: H * f }, { x: W * f, y: H - H * f }];
        }

        zoomIn(t) {
            this.full();
            const f = t / 2, W = this.W, H = this.H;
            this.pts = [{ x: W / 2 - W * f, y: H / 2 - H * f }, { x: W / 2 + W * f, y: H / 2 - H * f }, { x: W / 2 - W * f, y: H / 2 + H * f }];
        }

        zoomTopLeftIn(t) { this.full(); const m = this.W * t; this.pts[1].x = m; this.pts[2].y = m * this.H / this.W; }

        zoomTopLeftOut(t) {
            this.full();
            const m = this.W * t, y = m * this.H / this.W;
            this.pts[0] = { x: m, y };
            this.pts[1].y = y;
            this.pts[2].x = m;
        }

        turn2Out(t) { this.full(); const f = t / 2; this.pts[0] = { x: this.W * f, y: this.H * f }; }
        turn2In(t) { this.full(); const f = .5 - t / 2; this.pts[0] = { x: this.W * f, y: this.H * f }; }

        diamondOut(t) {
            this.full();
            const W = this.W, H = this.H;
            if (t < .5) {
                this.pts[0].x = W * t;
                this.pts[1].y = H * t;
                this.pts[2].y = H - H * t;
            } else {
                const u = t - .5;
                this.pts = [{ x: W / 2, y: H * u }, { x: W - W * u, y: H / 2 }, { x: W * u, y: H / 2 }];
            }
        }

        diamondIn(t) {
            this.full();
            const W = this.W, H = this.H;
            if (t < .5) {
                this.pts = [{ x: W / 2, y: H / 2 - H * t }, { x: W / 2 + W * t, y: H / 2 }, { x: W / 2 - W * t, y: H / 2 }];
            } else {
                const u = t - .5;
                this.pts[0].x = W / 2 - W * u;
                this.pts[1].y = H / 2 - H * u;
                this.pts[2].y = H / 2 + H * u;
            }
        }

        downRightOut(t) {
            this.full();
            const x = this.W * t, y = this.H * t;
            this.pts = [{ x, y }, { x: this.W, y }, { x, y: this.H }];
        }

        downRightIn(t) {
            this.full();
            this.pts = [{ x: 0, y: 0 }, { x: this.W * t, y: 0 }, { x: 0, y: this.H * t }];
        }

        upLeftOut(t) {
            this.full();
            this.pts = [{ x: 0, y: 0 }, { x: this.W - this.W * t, y: 0 }, { x: 0, y: this.H - this.H * t }];
        }

        upLeftIn(t) {
            this.full();
            const x = this.W - this.W * t, y = this.H - this.H * t;
            this.pts = [{ x, y }, { x: this.W, y }, { x, y: this.H }];
        }

        clockIn(t) { this.full(); this.clock = { from: 0, to: Math.PI * 2 * t }; }
        clockOut(t) { this.full(); this.clock = { from: Math.PI * 2 * t, to: Math.PI * 2 }; }

        butterfly(t, reverse) {
            this.full();
            const fak = reverse ? 1 - t : t;
            const W = this.W, H = this.H;
            this.poly = butterflyShape.map(([x, y]) => {
                const dx = x - .5, dy = y - .5;
                const fx = Math.abs(dx) < 1e-4 ? Infinity : .5 / Math.abs(dx);
                const fy = Math.abs(dy) < 1e-4 ? Infinity : .5 / Math.abs(dy);
                const f = Math.min(fx, fy);
                const rx = (.5 + dx * f) * W, ry = (.5 + dy * f) * H;
                return { x: rx + (x * W - rx) * fak, y: ry + (y * H - ry) * fak };
            });
        }

        butterflyShrink(t) {
            this.butterfly(1, false);
            const f = 1 - t, mx = this.W / 2, my = this.H / 2;
            for (const p of this.poly) {
                p.x = mx + (p.x - mx) * f;
                p.y = my + (p.y - my) * f;
            }
        }

        rollOut(t, dir) {
            this.full();
            const r = Math.hypot(this.W, this.H) / 2, dx = (r + this.fw / 2) * t;
            this.pos.x = this.baseX + dir * dx;
            rotate(this.pts, this.W / 2, this.H / 2, dx / r);
        }

        rollIn(t, dir) {
            this.full();
            const r = Math.hypot(this.W, this.H) / 2, dx = (r + this.fw / 2) * (1 - t);
            this.pos.x = this.baseX + dir * dx;
            rotate(this.pts, this.W / 2, this.H / 2, -dx / r);
        }

        rollShrinkOut(t) {
            this.full();
            const r = Math.hypot(this.W, this.H) / 2, dx = this.fw / 4 * t;
            this.pos.x = this.baseX + dx;
            const alpha = t < 1 ? dx / (r * (1 - t)) : 0;
            tran(this.pts, this.W / 2, this.H / 2, alpha, 1 - t, this.W / 2 + dx, this.H / 2);
        }

        rollGrowIn(t) {
            this.full();
            const r = Math.hypot(this.W, this.H) / 2, dx = (r + this.fw / 2) * (1 - t);
            this.pos.x = this.baseX - dx;
            const alpha = t > 0 ? -dx / (r * t) : 0;
            tran(this.pts, this.W / 2, this.H / 2, alpha, t, this.W / 2 - dx, this.H / 2);
        }

        spinShrink(t) { this.full(); tran(this.pts, this.W / 2, this.H / 2, Math.PI * 2 * t, 1 - t, this.W / 2, this.H / 2); }
        spinGrow(t) { this.full(); tran(this.pts, this.W / 2, this.H / 2, Math.PI * 2 * t, t, this.W / 2, this.H / 2); }
    }

    function clockPath(p) {
        const cx = p.W / 2, cy = p.H / 2, R = Math.hypot(p.W, p.H);
        const path = [{ x: cx, y: cy }];
        const steps = 72;
        for (let i = 0; i <= steps; i++) {
            const a = p.clock.from + (p.clock.to - p.clock.from) * i / steps - Math.PI / 2;
            path.push({ x: cx + Math.cos(a) * R, y: cy + Math.sin(a) * R });
        }
        return path;
    }

    function drawPic(ctx, p, n, dpr) {
        if (!p || !p.visible) return;
        const { W, H, img } = p;
        const tw = W / n, th = H / n, sw = img.width / n, sh = img.height / n;
        const clip = p.poly ?? (p.clock ? clockPath(p) : null);
        for (let i = 0; i < n; i++) {
            for (let j = 0; j < n; j++) {
                const left = i * tw, top = j * th;
                const map = q => ({ x: q.x * tw / W + left + p.pos.x, y: q.y * th / H + top + p.pos.y });
                const [a, b, c] = p.pts.map(map);
                ctx.save();
                if (clip) {
                    ctx.beginPath();
                    clip.forEach((q, k) => {
                        const m = map(q);
                        if (k) ctx.lineTo(m.x, m.y); else ctx.moveTo(m.x, m.y);
                    });
                    ctx.closePath();
                    ctx.clip();
                }
                ctx.setTransform(
                    dpr * (b.x - a.x) / sw, dpr * (b.y - a.y) / sw,
                    dpr * (c.x - a.x) / sh, dpr * (c.y - a.y) / sh,
                    dpr * a.x, dpr * a.y);
                ctx.drawImage(img, i * sw, j * sh, sw, sh, 0, 0, sw, sh);
                ctx.restore();
            }
        }
    }

    function makeTriangle(pts) {
        return {
            pts,
            cx: (pts[0].x + pts[1].x + pts[2].x) / 3,
            cy: (pts[0].y + pts[1].y + pts[2].y) / 3,
            delay: rnd(0, .22),
            speed: rnd(.85, 1.3),
            drift: rnd(-80, 80),
            rotSpeed: rnd(-90, 90),
            ox: 0, oy: 0, rot: 0
        };
    }

    function edgePoint(pos, p) {
        const { W, H } = p, x0 = p.baseX, y0 = p.baseY;
        if (pos < W) return { x: x0 + pos, y: y0 };
        pos -= W;
        if (pos < H) return { x: x0 + W, y: y0 + pos };
        pos -= H;
        if (pos < W) return { x: x0 + W - pos, y: y0 + H };
        pos -= W;
        return { x: x0, y: y0 + H - pos };
    }

    function edgeFan(p, n, withCorners) {
        const perimeter = 2 * (p.W + p.H);
        let positions;
        if (withCorners) {
            n = Math.max(4, n);
            positions = [0, p.W, p.W + p.H, 2 * p.W + p.H];
            const extra = n - 4;
            for (let i = 0; i < extra; i++) positions.push((i + .5) * perimeter / extra);
            positions.sort((a, b) => a - b);
        } else {
            n = Math.max(3, n);
            positions = Array.from({ length: n }, (_, i) => i * perimeter / n);
        }
        const center = { x: p.baseX + p.W / 2, y: p.baseY + p.H / 2 };
        return positions.map((pos, i) => makeTriangle([
            center, edgePoint(pos, p), edgePoint(positions[(i + 1) % positions.length], p)]));
    }

    function grid(p, n) {
        const [cols, rows] = n <= 20 ? [5, 2] : n <= 50 ? [5, 5] : [10, 5];
        const list = [];
        for (let y = 0; y < rows; y++) {
            for (let x = 0; x < cols; x++) {
                const l = p.baseX + x * p.W / cols, r = p.baseX + (x + 1) * p.W / cols;
                const t = p.baseY + y * p.H / rows, b = p.baseY + (y + 1) * p.H / rows;
                const ol = { x: l, y: t }, or = { x: r, y: t }, ul = { x: l, y: b }, ur = { x: r, y: b };
                const pair = Math.random() < .5
                    ? [[ol, or, ul], [or, ur, ul]]
                    : [[ol, or, ur], [ol, ur, ul]];
                for (const pts of pair) {
                    const d = makeTriangle(pts);
                    d.delay = 0;
                    list.push(d);
                }
            }
        }
        const mx = p.baseX + p.W / 2, my = p.baseY + p.H / 2, radius = Math.hypot(p.fw, p.fh);
        for (const d of list) {
            let dx = d.cx - mx, dy = d.cy - my, len = Math.hypot(dx, dy);
            if (len < .1) {
                const a = Math.random() * Math.PI * 2;
                dx = Math.cos(a); dy = Math.sin(a); len = 1;
            }
            dx /= len; dy /= len;
            const spread = d.drift / 700, dist = radius * (.9 + d.speed * .3);
            d.ex = (dx + spread) * dist;
            d.ey = (dy - Math.abs(spread) * .25) * dist;
        }
        return list;
    }

    function drawTriangles(ctx, st) {
        const tri = st.tri;
        if (!tri) return;
        const p = tri.src;
        for (const d of tri.list) {
            ctx.save();
            ctx.translate(d.cx + d.ox, d.cy + d.oy);
            ctx.rotate(d.rot * Math.PI / 180);
            ctx.translate(-d.cx, -d.cy);
            ctx.beginPath();
            ctx.moveTo(d.pts[0].x, d.pts[0].y);
            ctx.lineTo(d.pts[1].x, d.pts[1].y);
            ctx.lineTo(d.pts[2].x, d.pts[2].y);
            ctx.closePath();
            ctx.clip();
            ctx.drawImage(p.img, p.baseX, p.baseY, p.W, p.H);
            ctx.restore();
        }
    }

    // Rückgabewert true: zuerst Bild 1, dann Bild 2 zeichnen (Reihenfolge12 im Original).
    const half = (out, into) => (T, a, b) => {
        if (T < .5) { out(a, T * 2); b.hide(); }
        else { a.hide(); into(b, (T - .5) * 2); }
        return true;
    };
    const both = (out, into) => (T, a, b) => { out(a, T); into(b, T); return true; };
    const bg = out => (T, a, b) => { b.full(); out(a, T); return false; };
    const roll = (out, into) => (T, a, b) => { out(a, T); into(b, T); return false; };

    const fallBg = n => (T, a, b, st) => {
        b.full();
        st.tri ??= { src: a, list: edgeFan(a, n, true) };
        a.hide();
        for (const d of st.tri.list) {
            const l = clamp((T - d.delay) / (1 - d.delay));
            d.ox = l * d.drift;
            d.oy = l * l * (a.H + 140) * d.speed;
            d.rot = d.rotSpeed * l;
        }
        return false;
    };

    const fallReverse = n => (T, a, b, st) => {
        a.full();
        b.hide();
        st.tri ??= { src: b, list: edgeFan(b, n, false) };
        const rest = 1 - T * T * T;
        for (const d of st.tri.list) {
            d.ox = rest * d.drift;
            d.oy = rest * (b.H + 140) * d.speed;
            d.rot = d.rotSpeed * rest;
        }
        return true;
    };

    const explodeBg = n => (T, a, b, st) => {
        b.full();
        st.tri ??= { src: a, list: grid(a, n) };
        a.hide();
        const e = T * T * T;
        for (const d of st.tri.list) {
            d.ox = d.ex * e;
            d.oy = d.ey * e;
            d.rot = d.rotSpeed * e * 4;
        }
        return false;
    };

    const implode = n => (T, a, b, st) => {
        a.full();
        b.hide();
        if (!st.tri) {
            st.tri = { src: b, list: grid(b, n) };
            for (const d of st.tri.list) d.rotSpeed = rnd(-540, 540);
        }
        const rest = Math.pow(1 - T, 3);
        for (const d of st.tri.list) {
            d.ox = d.ex * rest;
            d.oy = d.ey * rest;
            d.rot = d.rotSpeed * rest * 4;
        }
        return true;
    };

    const animations = [
        roll((p, t) => p.rollOut(t, -1), (p, t) => p.rollIn(t, 1)),
        roll((p, t) => p.rollOut(t, 1), (p, t) => p.rollIn(t, -1)),
        roll((p, t) => p.rollOut(t, 1), (p, t) => p.rollIn(t, 1)),
        roll((p, t) => p.rollOut(t, -1), (p, t) => p.rollIn(t, -1)),
        roll((p, t) => p.rollShrinkOut(t), (p, t) => p.rollGrowIn(t)),
        roll((p, t) => p.spinShrink(t), (p, t) => p.spinGrow(t)),
        bg((p, t) => p.zoomOut(t)),
        bg((p, t) => p.clockOut(t)),
        fallBg(5), fallBg(30), fallBg(100),
        fallReverse(5), fallReverse(30), fallReverse(100),
        explodeBg(20), explodeBg(50), explodeBg(100),
        implode(20), implode(50), implode(100),
        bg((p, t) => p.turnOut(t)),
        bg((p, t) => p.outRight(t)),
        bg((p, t) => p.outTopDown(t)),
        bg((p, t) => p.zoomTopLeftOut(t)),
        bg((p, t) => p.turn2Out(t)),
        bg((p, t) => p.diamondOut(t)),
        bg((p, t) => p.downRightOut(t)),
        bg((p, t) => p.upLeftOut(t)),
        (T, a, b) => {
            b.full();
            if (T < .5) a.butterfly(T * 2, false); else a.butterflyShrink(T * 2 - 1);
            return false;
        },
        half((p, t) => p.clockOut(t), (p, t) => p.clockIn(t)),
        half((p, t) => p.zoomOut(t), (p, t) => p.zoomIn(t)),
        both((p, t) => p.turnOut(t), (p, t) => p.turnIn(t)),
        both((p, t) => p.outRight(t), (p, t) => p.inLeft(t)),
        both((p, t) => p.outTopDown(t), (p, t) => p.inTopDown(t)),
        both((p, t) => p.zoomTopLeftOut(t), (p, t) => p.zoomTopLeftIn(t)),
        half((p, t) => p.turn2Out(t), (p, t) => p.turn2In(t)),
        half((p, t) => p.diamondOut(t), (p, t) => p.diamondIn(t)),
        both((p, t) => p.downRightOut(t), (p, t) => p.downRightIn(t)),
        both((p, t) => p.upLeftOut(t), (p, t) => p.upLeftIn(t)),
        half((p, t) => p.butterfly(t, false), (p, t) => p.butterfly(t, true))
    ];

    const catalog = [
        ["AnimationReinRausRollen1", "Rein/Raus rollen 1", "Rollen"],
        ["AnimationReinRausRollen2", "Rein/Raus rollen 2", "Rollen"],
        ["AnimationReinRausRollen3", "Rein/Raus rollen 3", "Rollen"],
        ["AnimationReinRausRollen4", "Rein/Raus rollen 4", "Rollen"],
        ["AnimationReinRausRollenSkalieren", "Rein/Raus rollen skalieren", "Rollen"],
        ["AnimationRollenSkalieren", "Rollen skalieren", "Rollen"],
        ["AnimationZoomHG", "Zoom Hintergrund", "Hintergrund"],
        ["AnimationUhrHG", "Uhr Hintergrund", "Hintergrund"],
        ["AnimationDreieckeFallen5HG", "Dreiecke fallen 5", "Dreiecke fallen"],
        ["AnimationDreieckeFallen30HG", "Dreiecke fallen 30", "Dreiecke fallen"],
        ["AnimationDreieckeFallen100HG", "Dreiecke fallen 100", "Dreiecke fallen"],
        ["AnimationDreieckeFallenUmkehr5", "Dreiecke fallen Umkehr 5", "Dreiecke fallen"],
        ["AnimationDreieckeFallenUmkehr30", "Dreiecke fallen Umkehr 30", "Dreiecke fallen"],
        ["AnimationDreieckeFallenUmkehr100", "Dreiecke fallen Umkehr 100", "Dreiecke fallen"],
        ["AnimationDreieckeExplosion20HG", "Dreiecke Explosion 20", "Dreiecke Explosion"],
        ["AnimationDreieckeExplosion50HG", "Dreiecke Explosion 50", "Dreiecke Explosion"],
        ["AnimationDreieckeExplosion100HG", "Dreiecke Explosion 100", "Dreiecke Explosion"],
        ["AnimationDreieckeImplosion20", "Dreiecke Implosion 20", "Dreiecke Explosion"],
        ["AnimationDreieckeImplosion50", "Dreiecke Implosion 50", "Dreiecke Explosion"],
        ["AnimationDreieckeImplosion100", "Dreiecke Implosion 100", "Dreiecke Explosion"],
        ["AnimationDrehenHG", "Drehen Hintergrund", "Hintergrund"],
        ["AnimationTauschenLinksRechtsHG", "Tauschen links/rechts Hintergrund", "Hintergrund"],
        ["AnimationTauschenObenUntenHG", "Tauschen oben/unten Hintergrund", "Hintergrund"],
        ["AnimationZoomObenLinksHG", "Zoom oben links Hintergrund", "Hintergrund"],
        ["AnimationDrehen2HG", "Drehen 2 Hintergrund", "Hintergrund"],
        ["AnimationRauteHG", "Raute Hintergrund", "Hintergrund"],
        ["AnimationNachUntenRechtsHG", "Nach unten rechts Hintergrund", "Hintergrund"],
        ["AnimationNachObenLinksHG", "Nach oben links Hintergrund", "Hintergrund"],
        ["AnimationSchmetterlingHG", "Schmetterling Hintergrund", "Hintergrund"],
        ["AnimationUhr", "Uhr", "Klassisch"],
        ["AnimationZoom", "Zoom", "Klassisch"],
        ["AnimationDrehen", "Drehen", "Klassisch"],
        ["AnimationTauschenLinksRechts", "Tauschen links/rechts", "Klassisch"],
        ["AnimationTauschenObenUnten", "Tauschen oben/unten", "Klassisch"],
        ["AnimationZoomObenLinks", "Zoom oben links", "Klassisch"],
        ["AnimationDrehen2", "Drehen 2", "Klassisch"],
        ["AnimationRaute", "Raute", "Klassisch"],
        ["AnimationNachUntenRechts", "Nach unten rechts", "Klassisch"],
        ["AnimationNachObenLinks", "Nach oben links", "Klassisch"],
        ["AnimationSchmetterling", "Schmetterling", "Klassisch"]
    ];

    const storageKey = "coverPlayer.activeAnimations";
    let active = null;
    try {
        const saved = JSON.parse(localStorage.getItem(storageKey) ?? "null");
        if (Array.isArray(saved) && saved.length > 0) active = new Set(saved);
    } catch { }

    const isActive = i => active === null || active.has(catalog[i][0]);

    function nextAnimation() {
        for (let tries = 0; tries <= animations.length; tries++) {
            aniIndex++;
            if (aniIndex >= animations.length) {
                aniIndex = 0;
                subdivisions += 2;
                if (subdivisions > 8) subdivisions = 1;
            }
            if (isActive(aniIndex)) break;
        }
        return { fn: animations[aniIndex], n: subdivisions };
    }

    function getAnimations() {
        return catalog.map(([name, label, group], i) => ({ name, label, group, active: isActive(i) }));
    }

    function setActiveAnimations(names) {
        const valid = (names ?? []).filter(n => catalog.some(c => c[0] === n));
        active = valid.length === 0 || valid.length === catalog.length ? null : new Set(valid);
        try {
            if (active === null) localStorage.removeItem(storageKey);
            else localStorage.setItem(storageKey, JSON.stringify([...active]));
        } catch { }
    }

    function resize(canvas, st) {
        const dpr = window.devicePixelRatio || 1;
        const w = canvas.clientWidth, h = canvas.clientHeight;
        if (w !== st.w || h !== st.h || dpr !== st.dpr) {
            st.w = w; st.h = h; st.dpr = dpr;
            canvas.width = Math.max(1, Math.round(w * dpr));
            canvas.height = Math.max(1, Math.round(h * dpr));
            return true;
        }
        return false;
    }

    function clear(canvas, st) {
        const ctx = canvas.getContext("2d");
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.setTransform(st.dpr, 0, 0, st.dpr, 0, 0);
        ctx.imageSmoothingQuality = "high";
        return ctx;
    }

    function renderStatic(canvas, st) {
        const ctx = clear(canvas, st);
        if (st.current && st.w > 0 && st.h > 0)
            drawPic(ctx, new Pic(st.current, st.w, st.h), 1, st.dpr);
    }

    function frame(canvas, st, anim) {
        if (st.anim !== anim) return;
        const T = (performance.now() - anim.start) / anim.duration;
        if (T >= 1) {
            st.anim = null;
            st.tri = null;
            renderStatic(canvas, st);
            return;
        }
        const aFirst = anim.fn(T, anim.a, anim.b, st);
        const ctx = clear(canvas, st);
        if (aFirst) {
            drawPic(ctx, anim.a, anim.n, st.dpr);
            drawTriangles(ctx, st);
            drawPic(ctx, anim.b, anim.n, st.dpr);
        } else {
            drawPic(ctx, anim.b, anim.n, st.dpr);
            drawPic(ctx, anim.a, anim.n, st.dpr);
            drawTriangles(ctx, st);
        }
        requestAnimationFrame(() => frame(canvas, st, anim));
    }

    function loadImage(src) {
        return new Promise(resolve => {
            const img = new Image();
            img.onload = () => resolve(img);
            img.onerror = () => resolve(null);
            img.src = src;
        });
    }

    async function show(canvas, src, durationMs) {
        if (!canvas) return;
        let st = states.get(canvas);
        if (!st) {
            st = { current: null, anim: null, tri: null, token: 0, w: 0, h: 0, dpr: 1 };
            states.set(canvas, st);
            new ResizeObserver(() => {
                if (resize(canvas, st)) {
                    st.anim = null;
                    st.tri = null;
                    renderStatic(canvas, st);
                }
            }).observe(canvas);
        }

        const token = ++st.token;
        const img = src ? await loadImage(src) : null;
        if (token !== st.token) return;

        const previous = st.current;
        st.anim = null;
        st.tri = null;
        st.current = img;
        resize(canvas, st);

        if (!previous || !img || durationMs <= 0 || st.w <= 0 || st.h <= 0) {
            renderStatic(canvas, st);
            return;
        }

        const { fn, n } = nextAnimation();
        const anim = {
            fn, n,
            a: new Pic(previous, st.w, st.h),
            b: new Pic(img, st.w, st.h),
            start: performance.now(),
            duration: durationMs
        };
        st.anim = anim;
        requestAnimationFrame(() => frame(canvas, st, anim));
    }

    return { show, getAnimations, setActiveAnimations };
})();
