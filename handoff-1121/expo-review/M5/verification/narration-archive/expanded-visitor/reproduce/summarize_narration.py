"""Summarize the saved real-clock visitor receipts and display the forty observed station screens."""
from collections import Counter
import json
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFont


def load(path):return json.loads(path.read_text(encoding='utf8'))


def main():
    root=Path(sys.argv[1])
    audit_path=Path(sys.argv[2]) if len(sys.argv)>2 else root/'media-audit.json'
    audit=load(audit_path)
    visit=load(root/'visitor-route.json')
    assert visit.get('complete'),visit.get('errors')
    events=audit['events'];rows=visit['visits'];assert [v['id'] for v in rows]==[e['id'] for e in events]
    final=visit['final'];reports=[];medias={r['id']:r for r in audit['audio']};byid={r['id']:r for r in rows}
    for i,row in enumerate(rows):
        key=row['id'];v=final['records'][key];m=medias[key];shot=row['atPhoto'];assert row['finished']>=1 and (root/row['screenshot']).exists()
        assert shot['voice']['id']==key and shot['voice']['subtitle']==m['subtitle'] and shot['project']['slide']==m['slides'][0]
        assert v['full'] and max(v['lamps'])>.25 and 0<=v['peakZoom']<=.040001
        assert row['walk']['complete'] or row.get('detours',[])[-1]['complete'],key
        reports.append({'position':i+1,'id':key,'chapter':events[i]['chapter'],'date':events[i]['date'],'slide':shot['project']['slide'],
                        'firstProximity':row['firstProximity'],'totalCompleted':final['finished'].get(key),'photo':row['screenshot']})
        if events[i].get('views'):
            expected=events[i]['views']
            assert [item['texture'] for item in row['sequence']]==expected,key
            assert all(item['subtitle']==m['subtitle'] and (root/item['file']).is_file() for item in row['sequence']),key
            for j,mark in enumerate([0,.3,.7]):
                tr=v['textures'][j]
                assert tr['file']==expected[j] and mark-.005<=tr['fraction']<=mark+.045,(key,tr)
            assert row['restingAfter']['slide']==expected[1] and not row['restingAfter']['active'],key
            reports[-1].update({'decodedSequence':v['textures'][:3],'photographedSequence':row['sequence'],'returnedTo':expected[1]})
    t=ImageFont.truetype('C:/Windows/Fonts/georgia.ttf',19);f=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',14)
    for start in range(0,40,10):
        w,h=455,317;sheet=Image.new('RGB',(2*w,5*h),'#183c31');draw=ImageDraw.Draw(sheet)
        for j,(e,r) in enumerate(zip(events[start:start+10],rows[start:start+10])):
            x=(j%2)*w;y=(j//2)*h
            with Image.open(root/r['screenshot']) as shot:
                view=shot.resize((w-10,274),Image.Resampling.LANCZOS);sheet.paste(view,(x+5,y+2))
            draw.text((x+9,y+276),f"{e['position']:02}. {e['title']}",font=t,fill='#f3e7bf')
            draw.text((x+10,y+299),e['date'],font=f,fill='#d5dfbb')
        sheet.save(root/('visit-'+str(start+1).zfill(2)+'-to-'+str(start+10)+'.jpg'),quality=89)
    order=[v['id'] for v in final['voices'] if v['id'].startswith('ev_')]
    always={r['id'] for r in rows if not r.get('replayedForPhotograph')}
    replayed=Counter(q['id'] for q in visit['replays'])
    final_counts=dict(final.get('liveFinished',final['finished']))
    if events[0].get('views'):
        assert order[:40]==[e['id'] for e in events]
        assert len(final['feet'])==3 and visit['returnWalk'][-1]['complete']
    last_id=rows[-1]['id'];last_poll=final_counts.get(last_id)
    # The first saved run sampled every 120 ms. Its driver separately awaited
    # the actual ended-count increment before it could set complete=True.
    final_wait=visit['complete'] and visit['replays'][-1]['id']==last_id
    if 'liveFinished' not in final and final_wait and last_poll==rows[-1]['finished']:
        final_counts[last_id]=rows[-1]['finished']+1
    for row in reports:row['totalCompleted']=final_counts[row['id']]
    faults=[{'id':key,'finished':final_counts.get(key),'expected':1+replayed[key]} for key in byid if final_counts.get(key)!=1+replayed[key]]
    assert not faults,faults
    path_metres=sum(r['walk']['metres']+sum(s['metres'] for s in r.get('detours',[]))+sum(s['metres'] for s in r.get('sightApproaches',[])) for r in rows)
    path_metres+=sum(s['metres'] for replay in visit['replays'] for s in replay.get('approaches',[]))
    path_metres+=sum(s['metres'] for s in visit.get('returnWalk',[]))
    report={'recordings':audit['recordings'],'exactSubtitles':audit['completeClipAndSubtitleIdentity'],'exactBytes':audit['decodedOriginalHallBytesMatchStaging'],
            'audioBytes':sum(r['bytes'] for r in audit['audio']),'audioDecodedSeconds':round(sum(r['secondsDecoded'] for r in audit['audio']),3),
            'photos':len(audit['images']),'nativeSourceImageDimensions':sorted({tuple(r['sourcePixels']) for r in audit['images']}),
            'inGamePhotographDimensions':[1024,768],'pixelsCorrelationMinimum':min(r['downsampledPixelCorrelation'] for r in audit['images']),
            'photoPsnrDbMinimum':min(r['peakSignalToNoiseDb'] for r in audit['images']),'clipDurationDifferenceCounts':len(audit['roundedDurationExceptions']),
            'eventsDecodedSeconds':round(sum(medias[e['id']]['secondsDecoded'] for e in events),3),'visitWallSeconds':visit['wallSeconds'],
            'walkedMetres':round(path_metres,2),'visitedInOrder':[r['id'] for r in rows],'observedVoiceStartOrder':order,
            'initialAdmission':{'zone':visit['admission']['zone'],'feet':visit['admission']['feet'],'heard':visit['admission']['narration']['heard']},
            'nativeOcclusionObservations':final['blocked'],'maxWaitingVoiceQueue':final['maxQueue'],'recordedReplays':visit['replays'],
            'finalPhysicalReplay':{'id':last_id,'completedBeforeReplay':rows[-1]['finished'],'lastPeriodicCounter':last_poll,'waitedForActualEndedCounterToAdvance':final_wait},
            'onceOnlyCountsMatchAllFortyAfterExplicitReplays':not faults,'events':reports,'networkFailures':visit['badResponses'],'pageErrors':visit['errors'],
            'servedGeometry':visit.get('browserGeometry',{}),'gazeDwell':visit.get('gaze'),
            'peakSimultaneousNarrators':final.get('peakSimultaneousNarrators'),'visitedRooms':final.get('rooms',[]),
            'mainExhibitContinuationMetres':round(sum(s['metres'] for s in visit.get('returnWalk',[])),2),
            'finalFeet':final['feet'],'initialVoiceStartsMatchDateOrder':order[:40]==[e['id'] for e in events]}
    (root/'archive-verification.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
    print('Finished',len(rows),'real stations;',round(path_metres,2),'m;',len(report['nativeOcclusionObservations']),'blocked first approaches;',len(visit['replays']),'replays;',report['visitWallSeconds'],'s')


if __name__=='__main__':main()
