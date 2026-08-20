import { Stack, Text, Title } from '@mantine/core';
import { getSession } from '@/lib/api';
import { getT } from '@/i18n/server';
import { ChangePasswordForm } from './change-password-form';

export default async function ChangePasswordPage() {
  const t = await getT();
  const session = await getSession();
  const forced = session.mustChangePassword ?? false;

  return (
    <Stack maw={520}>
      <div>
        <Title order={2}>{forced ? t('password.chooseTitle') : t('password.changeTitle')}</Title>
        <Text c="dimmed" size="sm">
          {forced ? t('password.chooseBody') : t('password.changeBody')}
        </Text>
      </div>

      <ChangePasswordForm forced={forced} />
    </Stack>
  );
}
