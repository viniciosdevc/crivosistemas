<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

// A raiz não tem página própria: quem está logado vai para o painel, os demais para o login.
const auth = useAuthStore()
useRouter().replace(auth.me ? '/painel' : '/entrar')
</script>

<template><div /></template>
