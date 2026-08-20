'use client';

import { useState, useTransition } from 'react';
import {
  ActionIcon, Badge, Button, Group, Modal, MultiSelect, PasswordInput, Stack, Table,
  TableTbody, TableTd, TableTh, TableThead, TableTr, Text, TextInput, Tooltip,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { IconKey, IconLock, IconLockOpen, IconPlus, IconUserCog } from '@tabler/icons-react';
import { useT } from '@/i18n/client';
import type { Translate } from '@/i18n';

/** Role choices, described in the reader's language. */
const roleOptions = (t: Translate) => [
  { value: 'Admin', label: t('users.roleAdmin') },
  { value: 'Editor', label: t('users.roleEditor') },
];
import type { ManagedUser } from './page';
import { createUser, resetPassword, setEnabled, setRoles } from './actions';



export function UsersTable({ users, currentUserId }: { users: ManagedUser[]; currentUserId: string }) {
  const t = useT();
  const [creating, setCreating] = useState(false);
  const [resetting, setResetting] = useState<ManagedUser | null>(null);
  const [editingRoles, setEditingRoles] = useState<ManagedUser | null>(null);
  const [pending, startTransition] = useTransition();

  function report(result: { ok: boolean; message?: string }, success: string, onDone?: () => void) {
    if (!result.ok) {
      notifications.show({ color: 'red', title: t('common.refused'), message: result.message ?? t('categories.failed') });
      return;
    }
    notifications.show({ color: 'green', title: t('common.done'), message: success });
    onDone?.();
  }

  return (
    <>
      <Group>
        <Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>
          {t('users.new')}
        </Button>
      </Group>

      <Table striped highlightOnHover withTableBorder>
        <TableThead>
          <TableTr>
            <TableTh>{t('users.username')}</TableTh>
            <TableTh>{t('users.displayName')}</TableTh>
            <TableTh w={220}>{t('users.roles')}</TableTh>
            <TableTh w={200}>{t('users.state')}</TableTh>
            <TableTh w={140}>{t('users.actions')}</TableTh>
          </TableTr>
        </TableThead>
        <TableTbody>
          {users.map((user) => {
            const isSelf = user.id === currentUserId;

            return (
              <TableTr key={user.id}>
                <TableTd>
                  <Text size="sm" fw={500}>{user.userName}</Text>
                  <Text size="xs" c="dimmed">{user.email}</Text>
                </TableTd>
                <TableTd><Text size="sm">{user.displayName}</Text></TableTd>
                <TableTd>
                  <Group gap={4}>
                    {user.roles.length === 0 && <Text size="xs" c="dimmed">{t('users.noRoles')}</Text>}
                    {user.roles.map((role) => (
                      <Badge key={role} size="sm" color={role === 'Admin' ? 'grape' : 'blue'}>
                        {role}
                      </Badge>
                    ))}
                  </Group>
                </TableTd>
                <TableTd>
                  <Group gap={4}>
                    {isSelf && <Badge size="sm" variant="outline">{t('users.you')}</Badge>}
                    {user.isDisabled && <Badge size="sm" color="red">{t('users.disabled')}</Badge>}
                    {user.mustChangePassword && (
                      <Badge size="sm" color="yellow">{t('users.mustChangePassword')}</Badge>
                    )}
                  </Group>
                </TableTd>
                <TableTd>
                  <Group gap={4} wrap="nowrap">
                    <Tooltip label={t('users.resetPassword')}>
                      <ActionIcon variant="subtle" onClick={() => setResetting(user)}>
                        <IconKey size={16} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label={t('users.changeRoles')}>
                      <ActionIcon variant="subtle" onClick={() => setEditingRoles(user)}>
                        <IconUserCog size={16} />
                      </ActionIcon>
                    </Tooltip>
                    <Tooltip label={user.isDisabled ? t('users.enable') : t('users.disable')}>
                      {/* Disabling yourself is refused by the API; hiding it avoids the dead end. */}
                      <ActionIcon
                        variant="subtle"
                        color={user.isDisabled ? 'green' : 'red'}
                        disabled={isSelf && !user.isDisabled}
                        onClick={() =>
                          startTransition(async () =>
                            report(
                              await setEnabled(user.id, user.isDisabled),
                              user.isDisabled ? t('users.enabled') : t('users.disabledDone'),
                            ),
                          )
                        }
                      >
                        {user.isDisabled ? <IconLockOpen size={16} /> : <IconLock size={16} />}
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                </TableTd>
              </TableTr>
            );
          })}
        </TableTbody>
      </Table>

      <CreateUserModal
        opened={creating}
        pending={pending}
        onClose={() => setCreating(false)}
        onSubmit={(values) =>
          startTransition(async () =>
            report(await createUser(values), t('users.created'), () => setCreating(false)),
          )
        }
      />

      <ResetPasswordModal
        user={resetting}
        pending={pending}
        onClose={() => setResetting(null)}
        onSubmit={(password) =>
          startTransition(async () =>
            report(
              await resetPassword(resetting!.id, password),
              t('users.passwordReset'),
              () => setResetting(null),
            ),
          )
        }
      />

      <RolesModal
        user={editingRoles}
        pending={pending}
        onClose={() => setEditingRoles(null)}
        onSubmit={(roles) =>
          startTransition(async () =>
            report(
              await setRoles(editingRoles!.id, roles),
              t('users.rolesUpdated'),
              () => setEditingRoles(null),
            ),
          )
        }
      />
    </>
  );
}

function CreateUserModal({
  opened, pending, onClose, onSubmit,
}: {
  opened: boolean;
  pending: boolean;
  onClose: () => void;
  onSubmit: (values: { userName: string; email: string; displayName: string; password: string; roles: string[] }) => void;
}) {
  const t = useT();
  const form = useForm({
    initialValues: { userName: '', email: '', displayName: '', password: '', roles: ['Editor'] },
    validate: {
      userName: (v) => (v.trim() ? null : t('common.required')),
      email: (v) => (/^\S+@\S+\.\S+$/.test(v) ? null : t('users.emailInvalid')),
      displayName: (v) => (v.trim() ? null : t('common.required')),
      password: (v) => (v.length >= 8 ? null : t('users.passwordTooShort')),
    },
  });

  return (
    <Modal opened={opened} onClose={onClose} title={t('users.new')}>
      <form onSubmit={form.onSubmit(onSubmit)}>
        <Stack>
          <Text size="sm" c="dimmed">{t('users.createIntro')}</Text>

          <TextInput label={t('users.username')} placeholder="editor1" {...form.getInputProps('userName')} />
          <TextInput label={t('users.email')} placeholder="editor1@example.com" {...form.getInputProps('email')} />
          <TextInput label={t('users.displayName')} {...form.getInputProps('displayName')} />
          <PasswordInput
            label={t('users.tempPassword')}
            description={t('users.tempPasswordHelp')}
            {...form.getInputProps('password')}
          />
          <MultiSelect label={t('users.roles')} data={roleOptions(t)} {...form.getInputProps('roles')} />

          <Button type="submit" loading={pending}>{t('users.createSubmit')}</Button>
        </Stack>
      </form>
    </Modal>
  );
}

function ResetPasswordModal({
  user, pending, onClose, onSubmit,
}: {
  user: ManagedUser | null;
  pending: boolean;
  onClose: () => void;
  onSubmit: (password: string) => void;
}) {
  const t = useT();
  const form = useForm({
    initialValues: { password: '' },
    validate: { password: (v) => (v.length >= 8 ? null : t('users.passwordTooShort')) },
  });

  return (
    <Modal opened={!!user} onClose={onClose} title={t('users.resetTitle', { name: user?.userName ?? '' })}>
      <form onSubmit={form.onSubmit((v) => onSubmit(v.password))}>
        <Stack>
          <Text size="sm" c="dimmed">{t('users.resetIntro')}</Text>
          <PasswordInput label={t('users.resetField')} {...form.getInputProps('password')} />
          <Button type="submit" loading={pending}>{t('users.resetPassword')}</Button>
        </Stack>
      </form>
    </Modal>
  );
}

function RolesModal({
  user, pending, onClose, onSubmit,
}: {
  user: ManagedUser | null;
  pending: boolean;
  onClose: () => void;
  onSubmit: (roles: string[]) => void;
}) {
  const t = useT();
  const [roles, setRolesValue] = useState<string[]>([]);

  return (
    <Modal
      opened={!!user}
      onClose={onClose}
      title={t('users.rolesTitle', { name: user?.userName ?? '' })}
      // Seed from the user each time the modal opens rather than holding stale state.
      onEnterTransitionEnd={() => setRolesValue(user?.roles ?? [])}
    >
      <Stack>
        <MultiSelect label={t('users.roles')} data={roleOptions(t)} value={roles} onChange={setRolesValue} />
        <Text size="sm" c="dimmed">{t('users.rolesIntro')}</Text>
        <Button loading={pending} onClick={() => onSubmit(roles)}>{t('users.rolesSubmit')}</Button>
      </Stack>
    </Modal>
  );
}
