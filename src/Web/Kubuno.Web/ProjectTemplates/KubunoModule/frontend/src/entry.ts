/**
 * Entry point of the $moduletitle$ module bundle, loaded at run time by the Kubuno host. The host checks
 * `sdkVersion` (an incompatible module is rejected cleanly), then calls `register()`.
 */
import { RouteRegistry, SDK_VERSION } from '@kubuno/sdk'
import './index.css'
import App from './App'

export const sdkVersion = SDK_VERSION

export function register() {
  // module.toml's sidebar item points at /$moduleid$.
  RouteRegistry.register('$moduleid$', App)
}
