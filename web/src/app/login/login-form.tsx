'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Alert, Button, Paper, PasswordInput, Stack, TextInput, Title } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useT } from '@/i18n/client';

export function LoginForm() {
  const router = useRouter();
  const params = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const t = useT();

  const form = useForm({
    initialValues: { username: '', password: '' },
    validate: {
      username: (v) => (v.trim() ? null : t('common.required')),
      password: (v) => (v ? null : t('common.required')),
    },
  });

  async function submit(values: typeof form.values) {
    setBusy(true);
    setError(null);

    // Posts to our own server, never to the API directly — the tokens must not reach
    // the browser.
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    }).catch(() => null);

    setBusy(false);

    if (!response) {
      setError(t('common.serverUnreachable'));
      return;
    }

    if (!response.ok) {
      const body = (await response.json().catch(() => ({}))) as { error?: string };
      setError(body.error ?? t('login.failedBody'));
      return;
    }

    router.push(params.get('next') ?? '/categories');
    router.refresh();
  }

  return (
    <Paper withBorder shadow="sm" p="xl" radius="md" w={380}>
      <form onSubmit={form.onSubmit(submit)}>
        <Stack>
          <Title order={3}>{t('login.heading')}</Title>

          {error && <Alert color="red" title={t('login.failed')}>{error}</Alert>}

          <TextInput
            label={t('login.username')}
            placeholder="admin"
            autoComplete="username"
            data-autofocus
            {...form.getInputProps('username')}
          />
          <PasswordInput
            label={t('login.password')}
            autoComplete="current-password"
            {...form.getInputProps('password')}
          />

          <Button type="submit" loading={busy} fullWidth>
            {t('login.submit')}
          </Button>
        </Stack>
      </form>
    </Paper>
  );
}
