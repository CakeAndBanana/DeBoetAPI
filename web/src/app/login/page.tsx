import { Suspense } from 'react';
import { Center, Group, Loader, Stack, Text, ThemeIcon } from '@mantine/core';
import { IconLeaf } from '@tabler/icons-react';
import { getLocale } from '@/i18n/server';
import { translatorFor } from '@/i18n';
import { LoginForm } from './login-form';

export default async function LoginPage() {
  const t = translatorFor(await getLocale());

  return (
    <Center
      h="100vh"
      p="md"
      style={{
        background: 'linear-gradient(145deg, var(--mantine-color-myColor-0), var(--mantine-color-gray-0) 55%)',
      }}
    >
      {/* LoginForm reads the ?next= param, which forces client rendering — Next requires
          that to sit behind a Suspense boundary so the rest of the page can prerender. */}
      <Stack gap="lg" align="center">
        <Group gap="sm" wrap="nowrap">
          <ThemeIcon size={44} radius="md" variant="gradient" gradient={{ from: 'myColor.8', to: 'myColor.5', deg: 135 }}>
            <IconLeaf size={25} stroke={2.2} />
          </ThemeIcon>
          <Stack gap={0}>
            <Text fw={700} size="xl" lh={1.1}>{t('app.title')}</Text>
            <Text size="sm" c="dimmed">{t('app.subtitle')}</Text>
          </Stack>
        </Group>

        <Suspense fallback={<Loader />}>
          <LoginForm />
        </Suspense>
      </Stack>
    </Center>
  );
}
