import { Suspense } from 'react';
import { Box, Center, Group, Loader, Stack, Text, ThemeIcon } from '@mantine/core';
import { IconLeaf } from '@tabler/icons-react';
import { getLocale } from '@/i18n/server';
import { translatorFor } from '@/i18n';
import { ColorSchemeToggle } from '@/components/color-scheme-toggle';
import { LanguageSwitcher } from '@/components/language-switcher';
import { LoginForm } from './login-form';

export default async function LoginPage() {
  const t = translatorFor(await getLocale());

  return (
    <Center
      h="100vh"
      p="md"
      pos="relative"
      style={{
        background: 'linear-gradient(145deg, var(--panel-login-from), var(--panel-login-to) 55%)',
      }}
    >
      {/*
        In the corner rather than under the form: this page is the one place someone who cannot
        read the interface has to get past, so the way out of the wrong language should be where
        the eye goes first, not below the thing they cannot read.
      */}
      <Box pos="absolute" top="var(--mantine-spacing-md)" right="var(--mantine-spacing-md)">
        <Group gap={4} wrap="nowrap">
          <ColorSchemeToggle />
          <LanguageSwitcher />
        </Group>
      </Box>

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
