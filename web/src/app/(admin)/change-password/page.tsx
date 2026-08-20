import { Stack, Text, Title } from '@mantine/core';
import { getSession } from '@/lib/api';
import { ChangePasswordForm } from './change-password-form';

export default async function ChangePasswordPage() {
  const session = await getSession();
  const forced = session.mustChangePassword ?? false;

  return (
    <Stack maw={520}>
      <div>
        <Title order={2}>{forced ? 'Choose your password' : 'Change password'}</Title>
        <Text c="dimmed" size="sm">
          {forced
            ? 'Your password was set by an administrator, who also knows it. Choose your own before continuing.'
            : 'Changing your password signs you out everywhere else.'}
        </Text>
      </div>

      <ChangePasswordForm forced={forced} />
    </Stack>
  );
}
