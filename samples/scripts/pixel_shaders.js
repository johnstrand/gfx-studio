// Programmable Per-Pixel Shaders for GfxStudio
// Receives (u, v, ctx, x, y) and returns [r, g, b], [r, g, b, a], { r, g, b }, or { r, g, b, a } in [0.0, 1.0].

export function synthwaveSky(u, v, ctx) {
    // Horizon at v = 0.58
    const horizon = 0.58;
    if (v > horizon) {
        // Retro perspective grid floor
        const fy = (v - horizon) / (1.0 - horizon);
        const z = 1.0 / max(fy, 0.02);
        const gx = abs(fract((u - 0.5) * z * 1.8) - 0.5);
        const gz = abs(fract(z * 0.65 - ctx.t * 2.0) - 0.5);
        const line = max(
            smoothstep(0.06, 0.0, gx),
            smoothstep(0.08, 0.0, gz)
        ) * smoothstep(0.0, 0.25, fy);

        const baseR = mix(0.08, 0.95, line);
        const baseG = mix(0.02, 0.20, line);
        const baseB = mix(0.18, 0.85, line);
        return [baseR, baseG, baseB, 1.0];
    }

    // Sunset gradient sky with fBm cloud wisps
    const skyT = v / horizon;
    const clouds = ctx.fbm(u * 4.5, v * 6.0, ctx.t) * 0.18;
    const r = clamp(mix(0.09, 0.98, pow(skyT, 1.4)) + clouds, 0.0, 1.0);
    const g = clamp(mix(0.04, 0.32, pow(skyT, 1.8)) + clouds * 0.5, 0.0, 1.0);
    const b = clamp(mix(0.28, 0.55, skyT) + clouds, 0.0, 1.0);
    return { r, g, b, a: 1.0 };
}

export function retroSun(u, v, ctx) {
    const cx = ctx.params.center.x;
    const cy = ctx.params.center.y;
    const radius = ctx.params.radius;

    const d = distance(u, v, cx, cy);
    if (d > radius * 1.35) {
        return [0.0, 0.0, 0.0, 0.0];
    }

    // Soft outer corona glow
    if (d > radius) {
        const glow = pow(1.0 - (d - radius) / (radius * 0.35), 2.2) * 0.55;
        return [1.0, 0.35, 0.55, glow];
    }

    // Horizontal scanline cutouts across the lower half of the sun
    const localY = (v - (cy - radius)) / (radius * 2.0);
    if (localY > 0.45) {
        const stripePhase = fract(localY * 9.0);
        const cutThickness = (localY - 0.40) * 0.65;
        if (stripePhase < cutThickness) {
            return [0.0, 0.0, 0.0, 0.0];
        }
    }

    const r = 1.0;
    const g = clamp(mix(0.92, 0.18, localY), 0.0, 1.0);
    const b = clamp(mix(0.25, 0.62, pow(localY, 1.5)), 0.0, 1.0);
    const edgeAlpha = smoothstep(radius, radius - 0.008, d);
    return { r, g, b, a: edgeAlpha };
}
