import { vFovForAspect } from './cameraMath.js';
import { COURSE_INTRO_SECONDS, courseIntroShots, courseIntroShotAt, sampleCourseIntro } from './courseIntroMath.js';

/** Caller owns elapsed time and the chase target snapshot, so pause and loading never advance a take. */
export function createCourseIntro(track) {
  const shots = courseIntroShots(track);
  return {
    duration: COURSE_INTRO_SECONDS,
    shots,
    shotAt: courseIntroShotAt,
    apply(camera, elapsed, { gentle = false, target = null } = {}) {
      // Restore the complete target at the boundary: no residual cinematic lens or horizon at GO.
      if (elapsed >= COURSE_INTRO_SECONDS && target) {
        camera.position.copy(target.position); camera.quaternion.copy(target.quaternion);
        camera.up.copy(target.up); camera.fov = target.fov; camera.updateProjectionMatrix();
        return true;
      }
      const pose = sampleCourseIntro(track, shots, elapsed, gentle);
      camera.position.fromArray(pose.eye); camera.up.fromArray(pose.up); camera.lookAt(...pose.look);
      camera.fov = vFovForAspect(pose.fov, camera.aspect);
      if (target && pose.blend > 0) {
        camera.position.lerp(target.position, pose.blend);
        camera.quaternion.slerp(target.quaternion, pose.blend);
        camera.up.lerp(target.up, pose.blend).normalize();
        camera.fov += (target.fov - camera.fov) * pose.blend;
      }
      camera.updateProjectionMatrix();
      return elapsed >= COURSE_INTRO_SECONDS;
    },
  };
}
