#!/usr/bin/env python3
"""Synthetic rig-motion test fixtures; never derived from copyrighted characters or captures."""
from pathlib import Path
import hashlib
import json
import math
import argparse

ROOT=Path(__file__).resolve().parents[2]
DEST=ROOT/'Samples~'/'RiggedCharacterDemos'/'Data'


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=True, allow_nan=False, separators=(',', ':')).encode('ascii')


def channel(name, bone, path, axis, minimum, maximum, neutral, interp='linear', mode='',kind='float',enum_items=None):
    return {'number': 0, 'name':name, 'bone':bone, 'data_path':path, 'array_index':axis,
            'unit':'blender_unit' if path=='location' else 'unitless',
            'minimum':float(minimum), 'maximum':float(maximum), 'neutral':float(neutral),
            'value_type':kind, 'interpolation':interp, 'rotation_mode':mode,
            'enum_items': [] if enum_items is None else enum_items}


def build(name, rig, bones, frames=1024):
    ch=[]
    for bone in bones:
        if bone=='hips':
            for axis,letter in enumerate('xyz'):
                ch.append(channel(f'{bone}_location_{letter}',bone,'location',axis,-2,2,0))
        for axis,part in enumerate('wxyz'):
            ch.append(channel(f'{bone}_quat_{part}',bone,'rotation_quaternion',axis,-1,1,
                              1 if axis==0 else 0, 'quaternion','QUATERNION'))
    if name=='humanoid':
        ch.append(channel('hand_mode','hand_control','["mode"]',-1,0,1,0,'step',kind='integer',enum_items=[[0,'rest'],[1,'closed']]))
    for idx,c in enumerate(ch): c['number']=idx+1
    profile={'format':'dancerudiments.rig-profile', 'schema_version':1,
             'type_uri':f'syn://type/danceflow/motion/rig/custom/unity-{name}-demo',
             'model':f'unity_synthetic_{name}', 'version':1,
             'source':{'coordinate_system':'Unity Y-up synthetic pose controls','authoring':'DanceFlow.Unity deterministic test',
                       'synthetic':True,'not_a_rigify_profile':True}, 'channels':ch}
    digest=hashlib.sha256(canonical(profile)).hexdigest()
    rows=[]
    for pip in range(frames):
        beat=pip/64
        row=[]
        for c in ch:
            bone=c['bone']; path=c['data_path']
            if path=='location':
                v=(0.18*math.sin(beat*math.pi/2) if c['array_index']==0 else
                   0.10*(1-math.cos(beat*math.pi*2)) if c['array_index']==1 else 0)
            elif path=='rotation_quaternion':
                amplitude=0.22 if bone=='hips' else 0.55 if 'arm' in bone or bone=='tail' else 0.28
                direction=-1 if bone.endswith('_r') else 1
                angle=direction*amplitude*math.sin(beat*math.pi*(2 if bone!='spine' else 1))
                # all synthetic articulations rotate around source-local Z
                v=math.cos(angle/2) if c['array_index']==0 else math.sin(angle/2) if c['array_index']==3 else 0
            else:
                v= 1 if int(beat)%2==1 else 0
            value=2*(v-c['minimum'])/(c['maximum']-c['minimum'])-1
            row.append(round(value,12))
        rows.append(row)
    recording={'format':'dancerudiments.recording','schema_version':1,'pips_per_beat':64,
               'sample_dtype':'float64','sample_decimals':12,
               'name':f'unity_{name}_dance', 'dimensions':[c['name'] for c in ch],
               'clock':{'bpm':120.0,'bpm_source':'authored synthetic Unity test motion',
                        'bpm_status':'confirmed','beat_zero_seconds':0.0,'phase_status':'supplied','start_pip':0},
               'samples':rows,'provenance':{'synthetic':True,'origin':'Unity demo only','no_music_match':True}}
    wrapper={'format':'dancerudiments.rig-motion', 'schema_version':1,'type_uri':profile['type_uri'],
             'profile_sha256':digest,'profile':profile,'recording':recording}
    return wrapper,digest


def main():
    p=argparse.ArgumentParser();p.add_argument('--verify',action='store_true');args=p.parse_args()
    definitions={'humanoid':['hips','spine','arm_l','arm_r'],
                 'quadruped':['hips','spine','tail']}
    for name,bones in definitions.items():
        wrapper,profile_sha=build(name,name,bones)
        output=DEST/f'unity_{name}_demo.rig-motion.json'
        expected=canonical(wrapper)
        if args.verify:
            if output.read_bytes()!=expected:
                raise SystemExit('Fixture changed: '+str(output))
        else:
            DEST.mkdir(parents=True,exist_ok=True)
            output.write_bytes(expected)
        print(name, 'profile_SHA256', profile_sha,'bytes',len(expected),'rows',len(wrapper['recording']['samples']))
    print('Rig-motion synthetic fixture parity: PASS' if args.verify else 'Fixtures generated')

if __name__=='__main__':main()
