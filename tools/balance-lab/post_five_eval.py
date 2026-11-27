"""Paired post-five earned-value intervals from route-lab snapshots.

Usage: python3 post_five_eval.py TRIAL.jsonl CONTROL.jsonl OUT.json
The minute-five full state hashes must agree on every assigned seed. Dead players'
fixed inventories count; intervals after a match ends are excluded explicitly.
"""
import argparse
import json
import random
import statistics
from pathlib import Path
from route_tuning_eval import rows


def boot_ratio(pairs, seed=8641):
    if len(pairs) < 5:
        return None
    rng = random.Random(seed)
    out = []
    for _ in range(3000):
        draw = rng.choices(pairs, k=len(pairs))
        denom = sum(r['control_value'] for r in draw)
        if denom > 0:
            out.append(sum(r['trial_value'] for r in draw) / denom)
    out.sort()
    return [out[int(.025 * len(out))], out[int(.975 * len(out))]] if out else None


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('trial', type=Path)
    ap.add_argument('control', type=Path)
    ap.add_argument('out', type=Path)
    a = ap.parse_args()
    source = json.loads((Path(__file__).resolve().parents[2] / 'testing/farm_route_data.json').read_text())
    reward = {r['id']: r['stat'] for r in source['rewards']}
    weight = {b['id']: b['weights'] for pair in source['buildGoals'].values() for b in pair}
    def value(row):
        return sum(n * max(.25, weight[row['build_id']].get(reward[mob], 0)) for mob,n in row['stacks'])
    t, c = rows(a.trial), rows(a.control)
    if t.keys() != c.keys():
        raise ValueError('snapshot keys differ')
    seeds = sorted({seed for seed, _ in t})
    for seed in seeds:
        x,y = t[seed,5],c[seed,5]
        same_opening = (x['state_hash'] == y['state_hash']
                        if 'state_hash' in x and 'state_hash' in y
                        else all(x[k] == y[k] for k in (
                            'seed', 'minute', 'seat', 'faction', 'build_id', 'map_hash',
                            'alive', 'stacks', 'camp_clears', 'movement_distance_u',
                            'moved_ticks', 'ticks')))
        if not x['reached'] or not y['reached'] or not same_opening:
            raise ValueError(f'opening full state mismatch at seed {seed}')
    pairs=[]
    for seed in seeds:
        for start,end in [(5,10),(10,15)]:
            x0,x1,y0,y1=t[seed,start],t[seed,end],c[seed,start],c[seed,end]
            if not (x0['reached'] and x1['reached'] and y0['reached'] and y1['reached']):
                continue
            for field in ['seed','minute','seat','faction','build_id','map_hash']:
                if x1[field] != y1[field]: raise ValueError(f'mismatch {field} at {seed}/{end}')
            pairs.append(dict(seed=seed,start=start,end=end,faction=x1['faction'],build=x1['build_id'],
                trial_value=value(x1)-value(x0),control_value=value(y1)-value(y0),
                trial_alive=x1['alive'],control_alive=y1['alive'],
                delta_moving_s=(x1['moved_ticks']-x0['moved_ticks']-y1['moved_ticks']+y0['moved_ticks'])*.025,
                delta_clears=x1['camp_clears']-x0['camp_clears']-y1['camp_clears']+y0['camp_clears']))
    summary=[]
    for start,end in [(5,10),(10,15)]:
        for group in ['all',*sorted({r['faction'] for r in pairs}),*sorted({r['build'] for r in pairs})]:
            sub=[r for r in pairs if r['start']==start and r['end']==end and group in ('all',r['faction'],r['build'])]
            if not sub:continue
            tv=sum(r['trial_value'] for r in sub);cv=sum(r['control_value'] for r in sub)
            ratios=[r['trial_value']/r['control_value'] for r in sub if r['control_value']>0]
            summary.append(dict(start=start,end=end,group=group,assigned=len(sub),trial_value=tv,control_value=cv,
                ratio=tv/cv if cv else None,boot_interval=boot_ratio(sub),paired_median=statistics.median(ratios) if ratios else None,
                better=sum(r['trial_value']>r['control_value'] for r in sub),
                tied=sum(r['trial_value']==r['control_value'] for r in sub),
                worse=sum(r['trial_value']<r['control_value'] for r in sub),
                trial_alive=sum(r['trial_alive'] for r in sub),control_alive=sum(r['control_alive'] for r in sub),
                mean_delta_moving_s=statistics.mean(r['delta_moving_s'] for r in sub),
                mean_delta_clears=statistics.mean(r['delta_clears'] for r in sub)))
    a.out.parent.mkdir(parents=True,exist_ok=True)
    a.out.write_text(json.dumps({'summary':summary,'pairs':pairs},indent=2)+'\n')

if __name__=='__main__':main()
