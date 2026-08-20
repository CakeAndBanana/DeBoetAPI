'use client';

import { Alert, Button, Container, Stack } from '@mantine/core';
import { useT } from '@/i18n/client';

/**
 * Last resort for anything the data layer could not handle — chiefly a 403, which should be
 * unreachable now that non-admins are refused at login. Next hides the real message in
 * production builds, so this stays deliberately generic and offers a way out.
 */
export default function AdminError({ reset }: { error: Error; reset: () => void }) {
  const t = useT();

  return (
    <Container size="sm" pt="xl">
      <Stack>
        <Alert color="red" title={t('error.title')}>{t('error.body')}</Alert>
        <Button onClick={reset} w="fit-content">{t('common.tryAgain')}</Button>
      </Stack>
    </Container>
  );
}
