'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Alert, Button, Card, PasswordInput, Stack } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';

export function ChangePasswordForm({ forced }: { forced: boolean }) {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const form = useForm({
    initialValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
    validate: {
      currentPassword: (v) => (v ? null : 'Required'),
      newPassword: (v) => (v.length >= 8 ? null : 'At least 8 characters'),
      confirmPassword: (v, values) => (v === values.newPassword ? null : 'The passwords do not match'),
    },
  });

  async function submit(values: typeof form.values) {
    setBusy(true);
    setError(null);

    // Posted to our own server, which holds the bearer token — the browser never sees it.
    const response = await fetch('/api/auth/change-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      }),
    }).catch(() => null);

    setBusy(false);

    if (!response) {
      setError('Could not reach the server.');
      return;
    }

    if (!response.ok) {
      const body = (await response.json().catch(() => ({}))) as { error?: string };
      setError(body.error ?? 'Could not change the password.');
      return;
    }

    notifications.show({
      color: 'green',
      title: 'Password changed',
      message: 'Any other sessions have been signed out.',
    });

    router.push('/categories');
    router.refresh();
  }

  return (
    <Card withBorder>
      <form onSubmit={form.onSubmit(submit)}>
        <Stack>
          {forced && (
            <Alert color="yellow" title="A password change is required">
              There is no self-service recovery here, so if you forget this one an
              administrator has to reset it for you.
            </Alert>
          )}

          {error && <Alert color="red" title="Could not change the password">{error}</Alert>}

          <PasswordInput
            label={forced ? 'The password you were given' : 'Current password'}
            autoComplete="current-password"
            {...form.getInputProps('currentPassword')}
          />
          <PasswordInput
            label="New password"
            description="At least 8 characters."
            autoComplete="new-password"
            {...form.getInputProps('newPassword')}
          />
          <PasswordInput
            label="Confirm new password"
            autoComplete="new-password"
            {...form.getInputProps('confirmPassword')}
          />

          <Button type="submit" loading={busy}>Change password</Button>
        </Stack>
      </form>
    </Card>
  );
}
