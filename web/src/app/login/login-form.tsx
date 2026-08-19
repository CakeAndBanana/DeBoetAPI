'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Alert, Button, Paper, PasswordInput, Stack, TextInput, Title } from '@mantine/core';
import { useForm } from '@mantine/form';

export function LoginForm() {
  const router = useRouter();
  const params = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const form = useForm({
    initialValues: { username: '', password: '' },
    validate: {
      username: (v) => (v.trim() ? null : 'Required'),
      password: (v) => (v ? null : 'Required'),
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
      setError('Could not reach the server.');
      return;
    }

    if (!response.ok) {
      const body = (await response.json().catch(() => ({}))) as { error?: string };
      setError(body.error ?? 'Sign in failed.');
      return;
    }

    router.push(params.get('next') ?? '/categories');
    router.refresh();
  }

  return (
    <Paper withBorder shadow="sm" p="xl" radius="md" w={380}>
      <form onSubmit={form.onSubmit(submit)}>
        <Stack>
          <Title order={3}>DeBoet Admin</Title>

          {error && <Alert color="red" title="Sign in failed">{error}</Alert>}

          <TextInput
            label="Username or email"
            placeholder="admin"
            autoComplete="username"
            data-autofocus
            {...form.getInputProps('username')}
          />
          <PasswordInput
            label="Password"
            autoComplete="current-password"
            {...form.getInputProps('password')}
          />

          <Button type="submit" loading={busy} fullWidth>
            Sign in
          </Button>
        </Stack>
      </form>
    </Paper>
  );
}
