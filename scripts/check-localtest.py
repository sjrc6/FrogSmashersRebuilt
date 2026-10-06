#!/usr/bin/env python3
"""Verify four tiled UDP instances; run under xvfb-run with a 1920x1080 desktop."""
from pathlib import Path
import os,json,subprocess,signal,re,socket,time
from PIL import ImageGrab
root=Path(__file__).resolve().parents[1];out=root/'.build/checks/localtest';out.mkdir(parents=True,exist_ok=True)
settings=out/'settings/FrogSmashersRebuilt/settings.json';settings.parent.mkdir(parents=True,exist_ok=True)
initial=json.dumps(dict(Fullscreen=True,VSync=False,Volume=0.2));settings.write_text(initial)
env=dict(os.environ,LIBGL_ALWAYS_SOFTWARE='1',ALSOFT_DRIVERS='null',XDG_DATA_HOME=str(out/'settings'))
with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
cmd=['dotnet',str(root/'.build/bin/FrogSmashers.Client.Automation/release/FrogSmashers.Client.Automation.dll'),'--localtest','4','--tile','--port',str(port),'--no-audio','--frames','500','--result',str(out/'host.json')]
def tiled(windows):
 if len(windows)!=4:return False
 bounds=[tuple(map(int,w[2:])) for w in windows]
 if not all(w>0 and h>0 and x>=0 and y>=0 and x+w<=1920 and y+h<=1080 for w,h,x,y in bounds):return False
 return all(x+w<=ox or ox+ow<=x or y+h<=oy or oy+oh<=y
            for i,(w,h,x,y) in enumerate(bounds) for ow,oh,ox,oy in bounds[i+1:])
with (out/'launch.log').open('w') as log:
 p=subprocess.Popen(cmd,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
 try:
  windows=[]
  for _ in range(100):
   tree=subprocess.check_output(['xwininfo','-root','-tree'],text=True)
   windows=re.findall(r'(0x[0-9a-f]+) "Frog Smashers Rebuilt - Local Test ([1-4])/4[^"\n]*".*? (\d+)x(\d+)\+(-?\d+)\+(-?\d+)',tree)
   if tiled(windows):break
   assert p.poll() is None,(p.returncode,(out/'launch.log').read_text())
   time.sleep(.1)
  assert tiled(windows),tree
  print('Windows:',windows,flush=True)
  time.sleep(2)
  ImageGrab.grab().save(out/'tiled.png')
  p.wait(timeout=60)
  assert p.returncode==0,(out/'launch.log').read_text()
  result=json.loads((out/'host.json').read_text())
  players=[s['Player'] for s in result['LobbySlots'] if s['Player'] is not None]
  assert len(players)==4 and len({v['Peer'] for v in players})==4,result
  assert result['Error'] is None and not result['Fullscreen'],result
  assert settings.read_text()==initial,'Test windows overwrote saved settings'
  print('PASS: four tiled windows, four separate peers joined, saved fullscreen overridden without saving.')
 finally:
  try:os.killpg(p.pid,signal.SIGKILL)
  except ProcessLookupError:pass
  p.wait()
