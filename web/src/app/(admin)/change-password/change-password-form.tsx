'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Alert, Button, Card, PasswordInput, Stack } from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { useT } from '@/i18n/client';

export function ChangePasswordForm({ forced }: { forced: boolean }) {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const t = useT();

  const form = useForm({
    initialValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
    validate: {
      currentPassword: (v) => (v ? null : t('common.required')),
      newPassword: (v) => (v.length >= 8 ? null : t('users.passwordTooShort')),
      confirmPassword: (v, values) => (v === values.newPassword ? null : t('password.mismatch')),
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
      setError(t('common.serverUnreachable'));
      return;
    }

    if (!response.ok) {
      const body = (await response.json().catch(() => ({}))) as { error?: string };
      setError(body.error ?? t('password.failedBody'));
      return;
    }

    notifications.show({
      color: 'green',
      title: t('password.changed'),
      message: t('password.changedBody'),
    });

    router.push('/categories');
    router.refresh();
  }

  return (
    <Card withBorder>
      <form onSubmit={form.onSubmit(submit)}>
        <Stack>
          {forced && (
<Alert color="yellow" title={t('password.forcedTitle')}>{t('password.forcedBody')}</Alert>
          )}

          {error && <Alert color="red" title={t('password.failed')}>{error}</Alert>}

          <PasswordInput
            label={forced ? t('password.given') : t('password.current')}
            autoComplete="current-password"
            {...form.getInputProps('currentPassword')}
          />
          <PasswordInput
            label={t('password.new')}
            description={t('password.newHelp')}
            autoComplete="new-password"
            {...form.getInputProps('newPassword')}
          />
          <PasswordInput
            label={t('password.confirm')}
            autoComplete="new-password"
            {...form.getInputProps('confirmPassword')}
          />

          <Button type="submit" loading={busy}>{t('password.submit')}</Button>
        </Stack>
      </form>
    </Card>
  );
}
