"""Tiny local model stand-ins; no torch/model/GPU loading."""
import importlib.util
import json
from pathlib import Path
import sys
import unittest
from unittest import mock
import zipfile
import test_training_cli as fixtures


class TrainingExportTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.TrainingCliTests(); self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root; self.work = self.root / 'run'
        self.fixture.cli.init_workspace(self.fixture.engine, self.fixture.dataset, self.work, self.fixture.profile, make_links=False)
        script = Path(__file__).with_name('training_export.py')
        self.assertTrue(script.is_file(), 'Training result/export adapter missing')
        spec = importlib.util.spec_from_file_location('training_export', script)
        self.exporter = importlib.util.module_from_spec(spec); sys.path.insert(0, str(script.parent)); spec.loader.exec_module(self.exporter)
        self.weight = self.work / 'weights/SoVITS' / ('voicelite_' + self.fixture.document['id'][:12] + '_e8_s10.pth')
        self.weight.write_bytes(b'test finetuned weight')
        for name in ('G_233333333333.pth', 'D_233333333333.pth'):
            (self.work / 'features/logs_s2_v2ProPlus' / name).write_bytes(b'complete checkpoint stand-in')
        self.reference = self.fixture.document['clips'][0]['id']

    def result(self):
        result = self.exporter.collect_result(self.work, 8)
        state_path = self.work / 'run.json'; state = json.loads(state_path.read_text(encoding='utf-8'))
        state['epochs_completed'] = 8; state_path.write_text(json.dumps(state), encoding='utf-8')
        return result

    def test_result_requires_requested_epoch_weight_and_both_checkpoints(self):
        self.weight.unlink()
        with self.assertRaises(ValueError): self.exporter.collect_result(self.work, 8)
        self.weight.write_bytes(b'weight')
        (self.work / 'features/logs_s2_v2ProPlus/D_233333333333.pth').unlink()
        with self.assertRaises(ValueError): self.exporter.collect_result(self.work, 8)

    def test_role_zip_uses_result_hashes_and_reviewed_reference(self):
        result = self.result(); output = self.root / 'role.zip'
        self.exporter.export_role(self.work, '测试角色', self.reference, output)
        with zipfile.ZipFile(output) as archive:
            self.assertIsNone(archive.testzip()); data = json.loads(archive.read('character.json'))
            self.assertEqual('voicelite-role-experiment-0', data['schema'])
            self.assertEqual('测试角色', data['name']); self.assertEqual('测试校对', data['prompt_text'])
            self.assertEqual('zh', data['language']); self.assertEqual(8, data['training']['sovits_epochs'])
            self.assertFalse(data['similarity_listener_approved'])
            for item in data['files']:
                self.assertEqual(item['sha256'], self.exporter.hashlib.sha256(archive.read(item['path'])).hexdigest())
            self.assertEqual(result['sovits_sha256'], next(x['sha256'] for x in data['files'] if x['path'] == data['sovits']))
            self.assertNotIn(str(self.root), archive.read('character.json').decode('utf-8'))

    def test_changed_weight_bad_reference_and_existing_output_are_refused(self):
        self.result(); output = self.root / 'role.zip'; output.write_bytes(b'keep')
        with self.assertRaises((ValueError, FileExistsError)): self.exporter.export_role(self.work, 'test', self.reference, output)
        self.assertEqual(b'keep', output.read_bytes())
        with self.assertRaises(ValueError): self.exporter.export_role(self.work, 'test', 'foreign', self.root / 'new.zip')
        self.weight.write_bytes(b'changed')
        with self.assertRaises(ValueError): self.exporter.export_role(self.work, 'test', self.reference, self.root / 'new.zip')
        self.assertFalse((self.root / 'new.zip').exists())

    def test_failed_zip_does_not_leave_partial_or_destination(self):
        self.result(); output = self.root / 'role.zip'
        with mock.patch('zipfile.ZipFile.write', side_effect=OSError('simulated full disk')):
            with self.assertRaises(OSError): self.exporter.export_role(self.work, 'test', self.reference, output)
        self.assertFalse(output.exists()); self.assertEqual([], list(self.root.glob('*.partial')))

    def test_complete_checkpoint_validation_requires_optimizers_and_exact_epoch(self):
        verify = self.exporter.verify_checkpoints
        verify(lambda _: {'iteration': 8, 'optimizer': {'state': {1: 1}}}, self.work, 8)
        for checkpoint in ({'iteration': 7, 'optimizer': {'state': {1: 1}}}, {'iteration': 8, 'optimizer': None}, {'iteration': True, 'optimizer': {'state': 1}}):
            with self.assertRaises(ValueError): verify(lambda _: checkpoint, self.work, 8)

    def test_resume_proof_uses_actual_upstream_checkpoint_lines_not_nonexistent_loaded_g(self):
        verify = getattr(self.exporter, 'verify_resume_log', None)
        self.assertTrue(callable(verify), 'Verified upstream resume-log parser missing')
        log = "INFO:features:Loaded checkpoint 'C:\\work\\D_233333333333.pth' (iteration 8)\nINFO:features:Loaded checkpoint 'C:\\work\\G_233333333333.pth' (iteration 8)\nVOICELITE_VERIFIED_CHECKPOINTS 9\n"
        verify(log, 8, 9)
        for invalid in (log.replace('G_233333333333', 'other'), log.replace('iteration 8', 'iteration 7'),
                        log.replace('VOICELITE_VERIFIED_CHECKPOINTS 9', ''), log + 'INFO:features:loaded pretrained fallback\n'):
            with self.assertRaises(ValueError): verify(invalid, 8, 9)


if __name__ == '__main__': unittest.main()
