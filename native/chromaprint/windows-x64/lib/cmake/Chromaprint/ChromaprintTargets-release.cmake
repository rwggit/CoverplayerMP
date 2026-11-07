#----------------------------------------------------------------
# Generated CMake target import file for configuration "Release".
#----------------------------------------------------------------

# Commands may need to know the format version.
set(CMAKE_IMPORT_FILE_VERSION 1)

# Import target "Chromaprint::chromaprint" for configuration "Release"
set_property(TARGET Chromaprint::chromaprint APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(Chromaprint::chromaprint PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/lib/chromaprint.lib"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/bin/chromaprint.dll"
  )

list(APPEND _cmake_import_check_targets Chromaprint::chromaprint )
list(APPEND _cmake_import_check_files_for_Chromaprint::chromaprint "${_IMPORT_PREFIX}/lib/chromaprint.lib" "${_IMPORT_PREFIX}/bin/chromaprint.dll" )

# Commands beyond this point should not need to know the version.
set(CMAKE_IMPORT_FILE_VERSION)
