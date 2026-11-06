"""Verify owned training outputs and export a portable VoiceLite role ZIP."""
import hashlib
import json
import re
from pathlib import Path
import uuid
import wave
import zipfile

from training_cli import digest, load_run, plain, validate_dataset, verify_files, write_json


def verify_resume_log(output, previous_epoch, target_epoch):
    if 'loaded pretrained' in output or f'VOICELITE_VERIFIED_CHECKPOINTS {target_epoch}\n' not in output:
        raise ValueError('Complete-checkpoint restore/post-training verification was not proven')
    for name in ('G_233333333333.pth', 'D_233333333333.pth'):
        pattern = r"^INFO:[^:\r\n]+:Loaded checkpoint '[^\r\n]*[\\/]" + re.escape(name) + r"' \(iteration (\d+)\)\r?$"
        epochs = re.findall(pattern, output, re.MULTILINE)
        if epochs != [str(previous_epoch)]: raise ValueError('G/D restore did not match the completed input epoch')


def verify_checkpoints(loader, work, expected_epoch):
    for name in ('G_233333333333.pth', 'D_233333333333.pth'):
        path = Path(work) / 'features/logs_s2_v2ProPlus' / name; plain(path)
        checkpoint = loader(path)
        epoch = checkpoint.get('iteration')
        if type(epoch) is not int or epoch != expected_epoch or not checkpoint.get('optimizer'):
            raise ValueError('Complete optimizer checkpoints for the target epoch are required')
        del checkpoint


def references(work):
    data = validate_dataset(Path(work) / 'data')
    result = []
    for clip in data['clips']:
        with wave.open(str(Path(work) / 'data' / clip['wavePath']), 'rb') as audio:
            seconds = audio.getnframes() / audio.getframerate()
        if 3 <= seconds <= 10:
            result.append({'id': clip['id'], 'name': clip['sourceName'], 'seconds': seconds,
                           'text': clip['text'], 'language': clip['language']})
    return result


def collect_result(work, epochs):
    work, state = load_run(work); data = validate_dataset(work / 'data')
    if type(epochs) is not int or not 1 <= epochs <= 100 or state['name'] != 'voicelite_' + data['id'][:12]:
        raise ValueError('Invalid result epoch or experiment name')
    folder = work / 'weights/SoVITS'; plain(folder)
    weights = list(folder.glob(state['name'] + f'_e{epochs}_s*.pth'))
    if len(weights) != 1: raise ValueError('The requested epoch inference weight is missing or ambiguous')
    weight = weights[0]; plain(weight)
    if not 1 <= weight.stat().st_size <= 512 * 1024 * 1024: raise ValueError('Invalid inference weight size')
    for name in ('G_233333333333.pth', 'D_233333333333.pth'):
        checkpoint = work / 'features/logs_s2_v2ProPlus' / name; plain(checkpoint)
        if not checkpoint.is_file() or checkpoint.stat().st_size == 0: raise ValueError('Complete checkpoints are missing')
    result = {'schema': 'voicelite-training-result-1', 'epochs_completed': epochs,
              'dataset_sha256': digest(work / 'data/dataset.json'), 'sovits_path': weight.relative_to(work).as_posix(),
              'sovits_sha256': digest(weight), 'sovits_bytes': weight.stat().st_size, 'references': references(work)}
    write_json(work / 'result.json', result)
    return result


def export_role(work, name, reference_id, output):
    work, state = load_run(work); data = validate_dataset(work / 'data')
    if not isinstance(name, str) or not name.strip() or len(name) > 100 or any(ord(char) < 32 for char in name):
        raise ValueError('A valid role name of at most 100 characters is required')
    result_path = work / 'result.json'; plain(result_path)
    if result_path.stat().st_size > 2 * 1024 * 1024: raise ValueError('Result manifest too large')
    result = json.loads(result_path.read_text(encoding='utf-8'))
    if result.get('schema') != 'voicelite-training-result-1' or result['epochs_completed'] != state['epochs_completed'] or result['dataset_sha256'] != digest(work / 'data/dataset.json'):
        raise ValueError('Training result no longer matches the frozen run')
    weight_folder = work / 'weights/SoVITS'; plain(weight_folder)
    weight = work / result['sovits_path']; plain(weight)
    if weight.parent.resolve() != weight_folder.resolve() or weight.suffix != '.pth' or digest(weight) != result['sovits_sha256']:
        raise ValueError('Trained inference weight changed')
    eligible = references(work); selected = next((clip for clip in eligible if clip['id'] == reference_id), None)
    if selected is None: raise ValueError('Choose a reviewed 3-10 second reference from this run')
    profile = json.loads((work / 'training-profile.json').read_text(encoding='utf-8'))
    gpt_relative = 'GPT_SoVITS/pretrained_models/s1v3.ckpt'
    entries = [entry for entry in profile['models'] if entry['path'] == gpt_relative]
    if len(entries) != 1: raise ValueError('The audited stock GPT weight is required')
    verify_files(state['engine'], entries)
    by_id = {clip['id']: clip for clip in data['clips']}
    sources = {'weights/gpt.ckpt': Path(state['engine']) / gpt_relative, 'weights/sovits.pth': weight,
               'references/main.wav': work / 'data' / by_id[reference_id]['wavePath']}
    auxiliary = []
    for clip in [item for item in eligible if item['id'] != reference_id and item['language'] == selected['language']][:2]:
        relative = f'references/auxiliary-{len(auxiliary) + 1}.wav'; auxiliary.append(relative)
        sources[relative] = work / 'data' / by_id[clip['id']]['wavePath']
    files = [{'path': relative, 'bytes': source.stat().st_size, 'sha256': digest(source)} for relative, source in sources.items()]
    manifest = {'schema': 'voicelite-role-experiment-0', 'name': name.strip(), 'version': f"local-e{result['epochs_completed']}-{uuid.uuid4().hex[:8]}",
                'engine_family': 'GPT-SoVITS', 'model_version': 'v2ProPlus', 'api_sha256': profile['api_sha256'],
                'gpt': 'weights/gpt.ckpt', 'sovits': 'weights/sovits.pth', 'reference': 'references/main.wav',
                'prompt_text': selected['text'], 'language': selected['language'], 'auxiliary': auxiliary, 'files': files,
                'training': {'sovits_epochs': result['epochs_completed'], 'gpt_finetuning_used': False,
                             'unique_clips': len(data['clips']), 'dataset_sha256': result['dataset_sha256'], 'user_marked_reviewed': True},
                'similarity_listener_approved': False}
    output = Path(output).absolute(); plain(output); output = output.resolve()
    if output.exists(): raise FileExistsError('Choose a new ZIP filename; existing files are never overwritten')
    if output.suffix.lower() != '.zip' or output.is_relative_to(work) or output.is_relative_to(Path(state['engine']).resolve()):
        raise ValueError('Export a ZIP outside the training workspace and engine')
    temporary = output.with_name(output.name + '.' + uuid.uuid4().hex + '.partial')
    try:
        with zipfile.ZipFile(temporary, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
            for relative, source in sources.items(): archive.write(source, relative)
            archive.writestr('character.json', json.dumps(manifest, ensure_ascii=False, indent=2))
            archive.writestr('README.txt', '本机训练角色；保留原GPT并微调SoVITS。导入兼容VoiceLite即可使用。\n含参考与推理权重，不含完整训练恢复状态。音色和发音请试听确认；分享前确认素材及模型授权。\n')
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None: raise ValueError('ZIP integrity failed')
            for item in files:
                with archive.open(item['path']) as file:
                    if hashlib.file_digest(file, 'sha256').hexdigest() != item['sha256']: raise ValueError('Exported payload changed')
        # On Windows rename refuses an existing destination, including a racing writer.
        temporary.rename(output)
    finally:
        if temporary.exists(): temporary.unlink()
    return {'output': str(output), 'bytes': output.stat().st_size, 'sha256': digest(output)}
