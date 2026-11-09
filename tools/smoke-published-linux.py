#!/usr/bin/env python3
import os, tempfile, subprocess, socket, time, json, struct, sqlite3, hashlib
from pathlib import Path
repo=Path(__file__).resolve().parents[1]
scu=repo/'tools/DicomAgent.Scu/bin/Release/net8.0/DicomAgent.Scu.dll'
def call_scu(*args):
    r=subprocess.run(['dotnet',str(scu),*map(str,args)],capture_output=True,text=True,timeout=40)
    assert r.returncode==0, r.stdout+r.stderr
    return r.stdout.strip()
def management(data,op):
    with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as s:
        s.settimeout(10);s.connect(str(data/'management.sock'))
        b=json.dumps({'Operation':op}).encode();s.sendall(struct.pack('<i',len(b))+b)
        def receive(n):
            out=b''
            while len(out)<n:
                piece=s.recv(n-len(out));assert piece;out+=piece
            return out
        return json.loads(receive(struct.unpack('<i',receive(4))[0]))
with tempfile.TemporaryDirectory(prefix='dicom-published-') as directory:
    data=Path(directory); fixture=data/'synthetic.dcm'
    print(call_scu('generate',fixture))
    with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
    settings={'Dicom':{'AeTitle':'DICOM_AGENT','ListenAddress':'127.0.0.1','Port':port,'StoragePath':str(data/'storage')}}
    (data/'settings.json').write_text(json.dumps(settings))
    env=os.environ.copy();env['DICOM_AGENT_DATA']=str(data);env['DOTNET_ROOT']='/tmp/no-dotnet-runtime';env['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=str(data/'bundle')
    with (data/'host.log').open('w') as log:
        p=subprocess.Popen([str(repo/'publish/linux/host/DicomAgent.Host')],env=env,stdout=log,stderr=log)
        try:
            for i in range(200):
                assert p.poll() is None, (data/'host.log').read_text()
                if (data/'management.sock').exists():break
                time.sleep(.05)
            assert management(data,'status')['Status']['State']=='Running'
            print(call_scu('echo','127.0.0.1',port,'DICOM_AGENT'))
            print(call_scu('store','127.0.0.1',port,'DICOM_AGENT',fixture))
            print(call_scu('store','127.0.0.1',port,'DICOM_AGENT',fixture))
            with sqlite3.connect(data/'history.db') as db:
                row=db.execute('SELECT FilePath,FileHash,FileSize FROM Instances').fetchall()
            assert len(row)==1
            f=Path(row[0][0]);assert f.exists();assert hashlib.sha256(f.read_bytes()).hexdigest().upper()==row[0][1];assert f.stat().st_size==row[0][2]
            assert management(data,'status')['Status']['TotalReceivedFiles']==1
            assert len(management(data,'history')['History'])==1
            print('PUBLISHED_HOST_SMOKE: PASS (self-contained, C-ECHO, C-STORE x2, one file/row, SHA-256, IPC)')
        finally:
            p.terminate();p.wait(timeout=25)
        assert p.returncode==0,(data/'host.log').read_text()
        assert not (data/'management.sock').exists()
        print('SIGTERM_SHUTDOWN: PASS (exit 0, socket removed)')
