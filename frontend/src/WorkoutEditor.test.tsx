import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import WorkoutEditor from './WorkoutEditor';
import { api } from './api';
vi.mock('./api', () => ({ api: vi.fn() }));

beforeEach(() => vi.resetAllMocks());
test('a template prefills a draft rather than silently checking in', async () => {
  render(<WorkoutEditor today="2026-10-01" editor={{ fromTemplate: { id: 't', userId: 'u', name: 'Upper body', type: 'Strength', notes: '', exercises: [] } }} onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.getByLabelText(/Completed/)).not.toBeChecked();
  expect(screen.getByLabelText('Workout name')).toHaveValue('Upper body');
  await userEvent.click(screen.getByRole('button', { name: 'Save workout' }));
  expect(api).toHaveBeenCalledWith('/workouts', 'POST', expect.objectContaining({ completed: false, title: 'Upper body', date: '2026-10-01' }));
});
test('cardio and strength entry preserve their distinct measurements', async () => {
  const user = userEvent.setup();
  render(<WorkoutEditor today="2026-10-01" editor={{}} onSaved={vi.fn()} onCancel={vi.fn()} />);
  await user.type(screen.getByLabelText('Workout name'), 'Mixed session');
  await user.click(screen.getByRole('button', { name: 'Add strength' }));
  await user.type(screen.getByLabelText('Exercise name'), 'Squat');
  await user.click(screen.getByRole('button', { name: 'Add cardio' }));
  await user.type(screen.getAllByLabelText('Exercise name')[1], 'Running');
  await user.type(screen.getByLabelText('Distance (miles)'), '2.5');
  await user.click(screen.getByRole('button', { name: 'Save workout' }));
  expect(api).toHaveBeenCalledWith('/workouts', 'POST', expect.objectContaining({ exercises: [expect.objectContaining({ kind: 'Strength', sets: [{ reps: 8, weightLb: 0 }] }), expect.objectContaining({ kind: 'Cardio', durationMinutes: 30, distanceMiles: 2.5, sets: [] })] }));
});
test('failed saves keep the form open and show an actionable message', async () => {
  vi.mocked(api).mockRejectedValue(new Error('Completed workouts cannot be in the future.'));
  const saved = vi.fn();
  render(<WorkoutEditor today="2026-10-01" editor={{}} onSaved={saved} onCancel={vi.fn()} />);
  await userEvent.type(screen.getByLabelText('Workout name'), 'Test workout');
  await userEvent.click(screen.getByRole('button', { name: 'Save workout' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Completed workouts cannot be in the future.');
  expect(saved).not.toHaveBeenCalled();
});
