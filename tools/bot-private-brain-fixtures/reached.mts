// Projects one script's V8 precise coverage onto the source ranges a case executed (P-CI-GREEN-4 step 2, owner choice
// 2026-11-19: "Record reached or not, no counts"). V8 hit counts and whether a function reports block detail or only a
// function-level count both depend on lazy compilation and bytecode flushing, so they differ run to run; which code ran
// does not. Innermost range wins (V8 nests block ranges inside their function and functions inside their parents).
// Returns sorted, merged [start, end) offsets into the transformed source whose innermost covering range ran.
export function reachedRanges(functions: any[]): number[][] {
  const ranges: { s: number; e: number; hit: boolean }[] = [];
  for (const f of functions) for (const r of f.ranges) ranges.push({ s: r.startOffset, e: r.endOffset, hit: r.count > 0 });
  const cuts = [...new Set(ranges.flatMap((r) => [r.s, r.e]))].sort((a, b) => a - b);
  const out: number[][] = [];
  for (let i = 0; i + 1 < cuts.length; i++) {
    const a = cuts[i], b = cuts[i + 1];
    let best: { s: number; e: number; hit: boolean } | undefined;
    for (const r of ranges) if (r.s <= a && r.e >= b && (!best || r.e - r.s < best.e - best.s || (r.e - r.s === best.e - best.s && !r.hit))) best = r;
    if (!best || !best.hit) continue;
    const last = out[out.length - 1];
    if (last && last[1] === a) last[1] = b; else out.push([a, b]);
  }
  return out;
}
