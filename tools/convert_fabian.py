"""Convert official FABIAN SOFA to an embedded, unnormalized float32 FIR table.
Run with h5py + numpy. Does not alter angles, phase, gains or arrival times.
"""
from pathlib import Path
import argparse, struct, hashlib, json
root=Path(__file__).resolve().parents[1]
import h5py
import numpy as np
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('source',type=Path,help='Directory containing the two official FABIAN SOFA files')
parser.add_argument('--output',type=Path,default=root/'Core/Data/FABIAN.bin')
args=parser.parse_args();source=args.source;out=args.output
out.parent.mkdir(parents=True,exist_ok=True)
with h5py.File(source/'FABIAN_HRIR_measured_HATO_0.sofa') as f, h5py.File(source/'FABIAN_CTF_measured_inverted_smoothed.sofa') as c:
    ir=f['Data.IR'][...]; pos=f['SourcePosition'][...]; ctf=c['Data.IR'][...].reshape(-1)
    sr=int(f['Data.SamplingRate'][0])
    assert sr==44100 and int(c['Data.SamplingRate'][0])==sr
    assert np.all(f['Data.Delay'][...]==0) and np.all(c['Data.Delay'][...]==0)
    assert np.isfinite(ir).all() and ir.shape==(11950,2,256)
    with out.open('wb') as w:
        w.write(b'SFSHRTF1'); w.write(struct.pack('<iiii',sr,len(ir),ir.shape[-1],len(ctf)))
        w.write(ctf.astype('<f4').tobytes())
        for p,h in zip(pos,ir):
            w.write(struct.pack('<ff',*p[:2])); w.write(h.astype('<f4').tobytes())
    manifest={'dataset':'FABIAN measured HATO 0, release 2020-01-22','license':'CC BY 4.0',
      'sourceUrl':'https://sofacoustics.org/data/database/tu-berlin/','sampleRate':sr,'directions':len(ir),'taps':ir.shape[-1],
      'ctf':'official 1/3-octave smoothed minimum-phase inverse CTF, 256 taps',
      'conversion':'float64 to float32 only; original phase, gains, shared time reference and SOFA coordinates retained',
      'sourceSha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.sofa')},
      'outputSha256':hashlib.sha256(out.read_bytes()).hexdigest()}
    out.with_name('FABIAN-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps(manifest,indent=2))
